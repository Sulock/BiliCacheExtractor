// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace BiliExtractor
{
    public static class VerifiedDownload
    {
        sealed class RangeUnsupportedException : IOException { }
        public static bool ValidRange(string header, long start, long length, long total)
        {
            var match = Regex.Match(header ?? "", @"^bytes\s+(\d+)-(\d+)/(\d+)\s*$", RegexOptions.IgnoreCase);
            long first, last, size;
            return length > 0 && start >= 0 && total > 0 && start <= total - length && match.Success &&
                long.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out first) &&
                long.TryParse(match.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out last) &&
                long.TryParse(match.Groups[3].Value, NumberStyles.None, CultureInfo.InvariantCulture, out size) &&
                first == start && last == start + length - 1 && size == total;
        }
        static HttpWebRequest Request(string url)
        {
            var uri = new Uri(url);
            if (uri.Scheme != "https") throw new InvalidDataException("下载地址必须使用 HTTPS。");
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            ServicePointManager.DefaultConnectionLimit = Math.Max(ServicePointManager.DefaultConnectionLimit, 6);
            var request = (HttpWebRequest)WebRequest.Create(uri);
            request.UserAgent = "BiliCacheExtractor";
            request.Timeout = request.ReadWriteTimeout = 30000;
            request.ServicePoint.ConnectionLimit = Math.Max(request.ServicePoint.ConnectionLimit, 6);
            return request;
        }
        static bool Retryable(WebException ex)
        {
            var response = ex.Response as HttpWebResponse;
            if (response == null) return ex.Status != WebExceptionStatus.TrustFailure && ex.Status != WebExceptionStatus.SecureChannelFailure;
            int status = (int)response.StatusCode;
            return status == 408 || status == 429 || status >= 500;
        }
        static TimeSpan RetryDelay(WebException ex, int attempt)
        {
            var response = ex == null ? null : ex.Response as HttpWebResponse;
            int seconds;
            if (response != null && int.TryParse(response.Headers["Retry-After"], out seconds) && seconds >= 0)
                return TimeSpan.FromSeconds(Math.Min(1800, seconds));
            DateTime date;
            if (response != null && DateTime.TryParse(response.Headers["Retry-After"], CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out date))
                return TimeSpan.FromSeconds(Math.Min(1800, Math.Max(1, (date.ToUniversalTime() - DateTime.UtcNow).TotalSeconds)));
            return TimeSpan.FromSeconds(Math.Min(10, attempt + 1));
        }
        static async Task PartAsync(string url, long total, long offset, long length, string file,
            CancellationToken token, Action<long> progress, Action<string> status)
        {
            long have = 0;
            for (int attempt = 0; have < length && attempt < 12; attempt++)
            {
                token.ThrowIfCancellationRequested();
                have = File.Exists(file) ? new FileInfo(file).Length : 0;
                progress(have);
                if (have == length) return;
                var request = Request(url);
                request.AddRange(offset + have, offset + length - 1);
                WebException failure = null;
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    timeout.CancelAfter(TimeSpan.FromSeconds(90));
                    using (timeout.Token.Register(request.Abort))
                    {
                        try
                        {
                            using (var response = (HttpWebResponse)await request.GetResponseAsync().ConfigureAwait(false))
                            {
                                if (response.ResponseUri.Scheme != "https") throw new InvalidDataException("下载地址必须使用 HTTPS。");
                                if (response.StatusCode == HttpStatusCode.OK) throw new RangeUnsupportedException();
                                if (response.StatusCode != HttpStatusCode.PartialContent || !ValidRange(response.Headers["Content-Range"], offset + have, length - have, total))
                                    throw new InvalidDataException("下载响应范围不正确。");
                                using (var input = response.GetResponseStream())
                                using (var output = new FileStream(file, FileMode.Append, FileAccess.Write, FileShare.None))
                                {
                                    var buffer = new byte[65536];
                                    while (have < length)
                                    {
                                        int count;
                                        try { count = await input.ReadAsync(buffer, 0, (int)Math.Min(buffer.Length, length - have), timeout.Token).ConfigureAwait(false); }
                                        catch (IOException ex) { throw new WebException("下载连接中断。", ex, WebExceptionStatus.ReceiveFailure, null); }
                                        if (count == 0) throw new WebException("下载连接中断。", WebExceptionStatus.ReceiveFailure);
                                        await output.WriteAsync(buffer, 0, count, timeout.Token).ConfigureAwait(false);
                                        have += count; progress(have);
                                    }
                                }
                            }
                            return;
                        }
                        catch (OperationCanceledException) { token.ThrowIfCancellationRequested(); }
                        catch (WebException ex) {
                            token.ThrowIfCancellationRequested();
                            if (!Retryable(ex)) { if (ex.Response != null) ex.Response.Dispose(); throw; }
                            failure = ex;
                        }
                    }
                }
                // A timeout or interrupted connection retains the received prefix.
                status("下载中断，正在续传…");
                TimeSpan delay = RetryDelay(failure, attempt);
                if (failure != null && failure.Response != null) failure.Response.Dispose();
                await Task.Delay(delay, token).ConfigureAwait(false);
            }
            if (!File.Exists(file) || new FileInfo(file).Length != length) throw new IOException("下载未完成，请重试或选择本地版本。");
        }
        static async Task SingleAsync(string url, long total, string file, CancellationToken token, Action<long,long> progress)
        {
            var request = Request(url);
            using (token.Register(request.Abort))
            {
                try {
                    using (var response = (HttpWebResponse)await request.GetResponseAsync().ConfigureAwait(false))
                    using (var input = response.GetResponseStream())
                    using (var output = File.Create(file)) {
                        if (response.ResponseUri.Scheme != "https") throw new InvalidDataException("下载地址必须使用 HTTPS。");
                        if (response.ContentLength >= 0 && response.ContentLength != total) throw new InvalidDataException("下载大小与固定版本不符。");
                        long received = 0; var buffer = new byte[65536]; int count;
                        while ((count = await input.ReadAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false)) != 0) {
                            received += count;
                            if (received > total) throw new InvalidDataException("下载大小与固定版本不符。");
                            await output.WriteAsync(buffer, 0, count, token).ConfigureAwait(false); progress(received,total);
                        }
                        if (received != total) throw new IOException("下载未完成，请重试或选择本地版本。");
                    }
                } catch (WebException) { token.ThrowIfCancellationRequested(); throw; }
            }
        }
        public static async Task DownloadAsync(string url, long bytes, string file, CancellationToken token,
            Action<long,long> progress, Action<string> status)
        {
            if (bytes <= 0 || bytes > 1024L * 1024 * 1024) throw new ArgumentOutOfRangeException("bytes");
            token.ThrowIfCancellationRequested();
            const long chunkBytes = 4L * 1024 * 1024;
            int count = (int)((bytes + chunkBytes - 1) / chunkBytes);
            var files = new string[count]; var tasks = new List<Task>();
            long received = 0; var progressLock = new object();
            Exception firstFailure = null; int unsupported = 0;
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                deadline.CancelAfter(TimeSpan.FromMinutes(30));
                try {
                    using (var stop = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token))
                    using (var slots = new SemaphoreSlim(4)) {
                        for (int index = 0; index < count; index++) {
                            int part = index; long start = part * chunkBytes; long length = Math.Min(chunkBytes, bytes - start);
                            files[part] = file + ".part-" + part;
                            tasks.Add(Task.Run(async delegate {
                                await slots.WaitAsync(stop.Token).ConfigureAwait(false);
                                try {
                                    long reported = 0;
                                    await PartAsync(url, bytes, start, length, files[part], stop.Token, value => {
                                        lock (progressLock) { received += value - reported; reported = value; progress(received, bytes); }
                                    }, status).ConfigureAwait(false);
                                } catch (RangeUnsupportedException ex) {
                                    Interlocked.Exchange(ref unsupported, 1); Interlocked.CompareExchange(ref firstFailure, ex, null); stop.Cancel(); throw;
                                } catch (Exception ex) {
                                    Interlocked.CompareExchange(ref firstFailure, ex, null); stop.Cancel(); throw;
                                } finally { slots.Release(); }
                            }, stop.Token));
                        }
                        try { await Task.WhenAll(tasks).ConfigureAwait(false); } catch (Exception) { }
                    }
                    deadline.Token.ThrowIfCancellationRequested();
                    if (unsupported != 0) {
                        status("正在重新连接下载…"); progress(0, bytes);
                        await SingleAsync(url, bytes, file, deadline.Token, progress).ConfigureAwait(false);
                    } else {
                        if (firstFailure != null) throw firstFailure;
                        using (var output = File.Create(file)) {
                            foreach (string part in files) using (var input = File.OpenRead(part))
                                await input.CopyToAsync(output, 65536, deadline.Token).ConfigureAwait(false);
                        }
                        if (new FileInfo(file).Length != bytes) throw new InvalidDataException("下载大小与固定版本不符。");
                        progress(bytes,bytes);
                    }
                } catch (OperationCanceledException) {
                    token.ThrowIfCancellationRequested();
                    throw new TimeoutException("下载超时，请重试或选择本地版本。");
                }
                finally { foreach (string part in files) if (part != null && File.Exists(part)) File.Delete(part); }
            }
        }
    }
}
