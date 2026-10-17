"""Compile the game's Metal programs to metallibs ahead of time (runs on the macOS runner).

  python3 ci/precompile_metal.py compile <lcmetal.pack> <shard> <shards> <out shard file>
  python3 ci/precompile_metal.py merge <out lcmetallib.pack> <shard file> ...

Input (made privately by lc-ios-private/tools/extract_metal.py): 'LCMP' u32 version u32 count, then per program
u64 key, u8 kind, u32 source length, u32 zlib length, zlib source.
Shard file: per compiled program u64 key, u32 metallib length, metallib.
Output (read by the shell's MetalPrecompile.mm through mmap): 'LCML' u32 version=1 u32 count u32 0, then count
index entries (u64 key, u64 offset, u64 length) sorted by key, then the metallibs at 16-byte aligned offsets.
Logs only counts: the programs are derived from game data.
"""
import os, struct, subprocess, sys, tempfile, zlib
from concurrent.futures import ThreadPoolExecutor

METAL = ['xcrun', '-sdk', 'iphoneos', 'metal', '-std=ios-metal2.4', '-mios-version-min=16.0', '-w']


def read_pack(path):
    with open(path, 'rb') as f:
        magic, ver, n = struct.unpack('<4sII', f.read(12))
        assert magic == b'LCMP' and ver == 1
        for _ in range(n):
            key, kind, size, zl = struct.unpack('<8sBII', f.read(17))
            yield key, kind, f.read(zl)


def compile_one(key, z, tmp):
    stem = os.path.join(tmp, key.hex())
    with open(stem + '.metal', 'wb') as f:
        f.write(zlib.decompress(z))
    try:
        r = subprocess.run(METAL + [stem + '.metal', '-o', stem + '.metallib'], capture_output=True)
        if r.returncode != 0:
            return key, None
        with open(stem + '.metallib', 'rb') as f:
            return key, f.read()
    finally:
        for ext in ('.metal', '.metallib'):
            if os.path.exists(stem + ext):
                os.remove(stem + ext)


def compile_shard(pack, shard, shards, out):
    shard, shards = int(shard), int(shards)
    work = [(k, z) for i, (k, kind, z) in enumerate(read_pack(pack)) if i % shards == shard]
    ok = failed = 0
    with tempfile.TemporaryDirectory() as tmp, open(out, 'wb') as f, \
            ThreadPoolExecutor(os.cpu_count() or 3) as pool:
        for i, (key, lib) in enumerate(pool.map(lambda w: compile_one(w[0], w[1], tmp), work)):
            if lib is None:
                failed += 1
            else:
                f.write(key + struct.pack('<I', len(lib)) + lib)
                ok += 1
            if (i + 1) % 200 == 0:
                print('shard %d: %d/%d done' % (shard, i + 1, len(work)), flush=True)
    print('shard %d: %d compiled, %d failed' % (shard, ok, failed))


def merge(out, *parts):
    libs = {}
    for p in parts:
        with open(p, 'rb') as f:
            while True:
                h = f.read(12)
                if not h:
                    break
                key, n = struct.unpack('<8sI', h)
                libs[key] = f.read(n)
    keys = sorted(libs, key=lambda k: struct.unpack('<Q', k)[0])
    off = 16 + 24 * len(keys)
    with open(out, 'wb') as f:
        f.write(b'LCML' + struct.pack('<III', 1, len(keys), 0))
        index, cur = [], off
        for k in keys:
            cur += -cur % 16
            index.append((k, cur, len(libs[k])))
            cur += len(libs[k])
        for k, o, n in index:
            f.write(k + struct.pack('<QQ', o, n))
        for (k, o, n) in index:
            f.write(b'\0' * (o - f.tell()))
            f.write(libs[k])
    print('%d metallibs, %.1f MB' % (len(keys), os.path.getsize(out) / 1e6))


if __name__ == '__main__':
    if len(sys.argv) >= 3 and sys.argv[1] == 'compile' and len(sys.argv) == 6:
        compile_shard(*sys.argv[2:])
    elif len(sys.argv) >= 4 and sys.argv[1] == 'merge':
        merge(*sys.argv[2:])
    else:
        sys.exit(__doc__)
