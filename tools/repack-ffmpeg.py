"""Prepare the tested CI build for redistribution; executable bytes stay unchanged."""
import hashlib, json, pathlib, shutil, subprocess, sys, tarfile, tempfile, zipfile

stage = pathlib.Path(sys.argv[1]).resolve()
old = json.loads((stage/'artifacts.json').read_text())
def digest(path):
    h=hashlib.sha256()
    with open(path,'rb') as f:
        for block in iter(lambda:f.read(1024*1024),b''): h.update(block)
    return h.hexdigest()
for item in old['artifacts']:
    assert digest(stage/item['name']) == item['sha256'], item['name']
work=stage/'repack'
work.mkdir()
for item in old['artifacts']:
    with zipfile.ZipFile(stage/item['name']) as z: z.extractall(work)
kit=work/'source-kit'
runtime=work/'FFmpeg-8.1.3-bce-nv12.2-win-x64'
for source in sorted(kit.glob('*-source.tar.gz')):
    print(source.name, source.stat().st_size, flush=True)
# Only AMF public headers are compiled. Retain those inputs and upstream notices,
# excluding unused SDK samples, media and third-party binaries from the source kit.
amf=kit/'amf-source.tar.gz'
trimmed=kit/'amf-headers.tar.gz'
with tarfile.open(amf,'r:gz') as original, tarfile.open(trimmed,'w:gz') as output:
    for member in original:
        relative=pathlib.PurePosixPath(member.name)
        if member.name.startswith('amf/amf/public/include/') or (
            len(relative.parts)==2 and relative.name.lower().startswith(('license','copying','copyright','readme'))):
            output.addfile(member, original.extractfile(member) if member.isfile() else None)
trimmed.replace(amf)
notices=runtime/'licenses'/'compiler-runtime'
notices.mkdir(parents=True)
for pattern in ('*mingw*/copyright','gcc-*-base/copyright'):
    for file in pathlib.Path('/usr/share/doc').glob(pattern):
        if file.is_file(): shutil.copy2(file,notices/(file.parent.name+'-copyright'))
exception=pathlib.Path('/usr/share/doc/gcc-13-base/C++/README.libstdc++-baseline.amd64')
# Preserve the runtime exception text shipped by the compiler package when available.
for file in pathlib.Path('/usr/share/doc').glob('gcc-*/copyright'):
    if file.is_file() and 'RUNTIME LIBRARY EXCEPTION' in file.read_text(errors='replace').upper():
        shutil.copy2(file,notices/(file.parent.name+'-copyright'))
shutil.copytree(notices,kit/'compiler-runtime-notices')
original=kit/'build.sh'
shutil.copy2(original,kit/'build-used-in-ci.sh')
text=original.read_text()
needle='cp "$recipe_dir/../ffmpeg-builds.LICENSE" "$work/source-kit/BtbN-recipes.LICENSE"'
assert needle in text
text=text.replace(needle,
    'if [[ -f "$recipe_dir/BtbN-recipes.LICENSE" ]]; then\n'
    '  cp "$recipe_dir/BtbN-recipes.LICENSE" "$work/source-kit/BtbN-recipes.LICENSE"\n'
    'else\n'+needle+'\nfi')
original.write_text(text)
hashes={p.name:dict(bytes=p.stat().st_size,sha256=digest(p)) for p in kit.glob('*-source.tar.gz')}
(kit/'SOURCE-ARCHIVES.json').write_text(json.dumps(hashes,indent=2)+'\n')
readme='''# FFmpeg 8.1.3 / NVENC API 12.2 source and build kit

The Windows executable files were built by GitHub Actions run 42353389139,
at BiliCacheExtractor commit a99e65056917d714082f599ee4e8cdd3c636b7e1.
Their byte hashes are recorded in the runtime BUILD-INFO.json.

sources.json pins eight upstream components. This kit contains their source
archives and the applied x265/libvpl build patches. For AMF, only the complete
public headers used by this build and its root notices are included; unused
SDK sample programs, sample media and third-party binaries are excluded.
SOURCE-ARCHIVES.json records the distributed source archive hashes.

Rebuild on Ubuntu 24.04 with Git, Python 3, make, pkg-config, cmake, ninja-build,
meson, nasm, gcc-mingw-w64-x86-64-posix and g++-mingw-w64-x86-64-posix installed:

    bash build.sh

The script fetches the exact repositories/revisions listed in sources.json,
builds the dependencies and FFmpeg, and writes runtime/source ZIP files under
$BCE_FFMPEG_WORK/out (default: /tmp/bce-ffmpeg-8.1.3/out). Network access is
needed by this convenience build script. The archived source trees are also
provided locally for inspection and independent builds.

build-used-in-ci.sh is the exact original CI script. build.sh adds only a
standalone source-kit license-file lookup; compiler options are unchanged.
build-environment.txt records the original Ubuntu packages. Runtime library
copyright notices are in compiler-runtime-notices/.

FFmpeg is GPL-3.0-or-later. x264/x265 are GPL-2.0-or-later; dav1d is BSD-2-Clause;
nv-codec-headers, AMF and libvpl use MIT; zlib uses the zlib license. Each project
retains its original license and copyright files in its source archive.
The BtbN recipe notice is retained for the dependency configuration adapted
from that build system. Graphics driver libraries are supplied by the system.
'''
(kit/'README.md').write_text(readme)
(runtime/'SOURCE-INFO.txt').write_text(
    'FFmpeg 8.1.3 with NVENC API 12.2. GPL-3.0-or-later.\n'
    'Corresponding source and all configured dependency inputs:\n'
    'https://github.com/Sulock/BiliCacheExtractor/releases/download/v1.4.0/FFmpeg-8.1.3-bce-nv12.2-source-kit.zip\n'
    'Build instructions and scope are in the source kit README.md.\n')
for binary in old['build']['runtimeFiles']:
    assert digest(runtime/binary['path']) == binary['sha256'], 'Executable changed'
out=stage/'out'
out.mkdir()
def archive(folder,name):
    target=out/name
    with zipfile.ZipFile(target,'w',zipfile.ZIP_DEFLATED,compresslevel=9) as z:
        for file in sorted(folder.rglob('*')):
            if file.is_file(): z.write(file,pathlib.Path(folder.name)/file.relative_to(folder))
    return dict(name=name,bytes=target.stat().st_size,sha256=digest(target))
files=[archive(runtime,runtime.name+'.zip'),archive(kit,'FFmpeg-8.1.3-bce-nv12.2-source-kit.zip')]
(out/'artifacts.json').write_text(json.dumps(dict(build=old['build'],artifacts=files,
    buildRun=42353389139,buildCommit='a99e65056917d714082f599ee4e8cdd3c636b7e1'),indent=2)+'\n')
(out/'SHA256SUMS.txt').write_text(''.join(f['sha256']+'  '+f['name']+'\n' for f in files))
print(json.dumps(files,indent=2),flush=True)
