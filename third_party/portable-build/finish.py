import hashlib, json, pathlib, shutil, subprocess, sys, zipfile

root = pathlib.Path(sys.argv[1])
manifest = json.loads(pathlib.Path(sys.argv[2]).read_text())
package = root / 'FFmpeg-8.1.3-bce-nv12.2-win-x64'
(package / 'bin').mkdir(parents=True, exist_ok=True)
(package / 'licenses').mkdir(exist_ok=True)
runtime = []
for name in ('ffmpeg.exe', 'ffprobe.exe'):
    target = package / 'bin' / name
    shutil.copy2(root / 'runtime' / 'bin' / name, target)
    imports = subprocess.check_output(['x86_64-w64-mingw32-objdump','-p',str(target)], text=True)
    dlls = [line.split(':',1)[1].strip() for line in imports.splitlines() if 'DLL Name:' in line]
    if any(x.lower().startswith(('libgcc','libstdc++','libwinpthread')) for x in dlls):
        raise RuntimeError('Compiler runtime must be linked statically: '+repr(dlls))
    runtime.append(dict(path='bin/'+name, bytes=target.stat().st_size,
                        sha256=hashlib.sha256(target.read_bytes()).hexdigest(), imports=dlls))
for name in manifest:
    source = root / 'src' / name
    destination = package / 'licenses' / name
    destination.mkdir(exist_ok=True)
    for path in source.iterdir():
        if path.is_file() and path.name.lower().startswith(('copying','license','copyright')):
            shutil.copy2(path, destination / path.name)
    if name == 'zlib': shutil.copy2(source/'README', destination/'README')
shutil.copy2(root/'src'/'ffmpeg'/'COPYING.GPLv3', package/'LICENSE.txt')
shutil.copy2(root/'source-kit'/'BtbN-recipes.LICENSE', package/'licenses'/'BtbN-recipes.LICENSE')
info = dict(version='8.1.3-bce-nv12.2', license='GPL-3.0-or-later', nvencApi='12.2',
            minimumNvidiaWindowsDriver='551.76', runtimeFiles=runtime, sources=manifest)
(package/'BUILD-INFO.json').write_text(json.dumps(info,indent=2)+'\n')
(package/'SOURCE-INFO.txt').write_text(
    'FFmpeg 8.1.3 with NVENC API 12.2. FFmpeg and its compiled dependencies retain their own licenses.\n'
    'Corresponding source: FFmpeg-8.1.3-bce-nv12.2-source-kit.zip alongside this runtime download.\n'
    'Source kit includes all eight pinned project archives, applied patches and build instructions.\n'
    'Run build.sh on Ubuntu 24.04 with the packages named in the GitHub workflow.\n')
out = root/'out'
def archive(folder, filename):
    target=out/filename
    with zipfile.ZipFile(target,'w',zipfile.ZIP_DEFLATED,compresslevel=9) as z:
        for file in sorted(folder.rglob('*')):
            if file.is_file(): z.write(file, pathlib.Path(folder.name)/file.relative_to(folder))
    return dict(name=filename,bytes=target.stat().st_size,sha256=hashlib.sha256(target.read_bytes()).hexdigest())
files=[archive(package,package.name+'.zip'), archive(root/'source-kit','FFmpeg-8.1.3-bce-nv12.2-source-kit.zip')]
(out/'artifacts.json').write_text(json.dumps(dict(build=info,artifacts=files),indent=2)+'\n')
(out/'SHA256SUMS.txt').write_text(''.join(x['sha256']+'  '+x['name']+'\n' for x in files))
print(json.dumps(files,indent=2))
