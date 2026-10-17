// Precompiled Metal programs.
//
// Unity compiles every Metal program from source the first time it is used, on its render thread, while the main
// thread waits. The game's HDRP programs are huge, so loading a level stalled the main thread for over 10 s and iOS
// killed the app (0x8BADF00D, scene-update watchdog). The game's programs are compiled ahead of time on CI
// (ci/precompile_metal.py) into Data/Raw/lcmetallib.pack; this swaps -[MTLDevice newLibraryWithSource:options:error:]
// so a source found in the pack (by the first 8 bytes of its SHA-256) is loaded from its metallib instead.
// Anything not in the pack is compiled from source as before. Hits, misses and slow compiles go to the native log.
#import <Foundation/Foundation.h>
#import <Metal/Metal.h>
#include <objc/runtime.h>
#include <CommonCrypto/CommonDigest.h>
#include <mach/mach_time.h>
#include <sys/mman.h>
#include <sys/stat.h>
#include <fcntl.h>
#include <unistd.h>
#include <string.h>
#include <stdint.h>

#if __has_feature(objc_arc)
#define LC_RETURN_NEW(x) ((__bridge_retained void *)(x))
#else
#define LC_RETURN_NEW(x) ((void *)(x))
#endif

struct LCMetalEntry { uint64_t key, offset, length; };

static const uint8_t *gPack;
static const LCMetalEntry *gIndex;
static uint32_t gCount;
static void *(*gOrigNewLibraryWithSource)(id, SEL, NSString *, MTLCompileOptions *, NSError **);
static volatile int32_t gHits, gMisses, gLoadFailures;

static const LCMetalEntry *LCMetalFind(uint64_t key)
{
    uint32_t lo = 0, hi = gCount;
    while (lo < hi)
    {
        uint32_t mid = (lo + hi) / 2;
        if (gIndex[mid].key < key) lo = mid + 1;
        else hi = mid;
    }
    return lo < gCount && gIndex[lo].key == key ? &gIndex[lo] : NULL;
}

static double LCMetalMs(uint64_t t0)
{
    static mach_timebase_info_data_t tb;
    if (tb.denom == 0) mach_timebase_info(&tb);
    return (double)(mach_absolute_time() - t0) * tb.numer / tb.denom / 1e6;
}

static void *LCNewLibraryWithSource(id self, SEL _cmd, NSString *source, MTLCompileOptions *options, NSError **error)
{
    static dispatch_once_t once;
    dispatch_once(&once, ^{
        NSLog(@"[metal] Unity compile options: language=0x%lx fastMath=%d macros=%lu",
              (unsigned long)options.languageVersion, (int)options.fastMathEnabled, (unsigned long)options.preprocessorMacros.count);
    });
    const char *utf8 = source ? [source UTF8String] : NULL;
    size_t length = utf8 ? strlen(utf8) : 0;
    if (gCount && utf8)
    {
        unsigned char digest[CC_SHA256_DIGEST_LENGTH];
        CC_SHA256(utf8, (CC_LONG)length, digest);
        uint64_t key;
        memcpy(&key, digest, sizeof(key));
        const LCMetalEntry *e = LCMetalFind(key);
        if (e)
        {
            dispatch_data_t data = dispatch_data_create(gPack + e->offset, (size_t)e->length, NULL, ^{});   // no copy: the pack stays mapped
            NSError *loadError = nil;
            id<MTLLibrary> lib = [(id<MTLDevice>)self newLibraryWithData:data error:&loadError];
#if !__has_feature(objc_arc)
            dispatch_release(data);
#endif
            if (lib)
            {
                int32_t hits = __sync_add_and_fetch(&gHits, 1);
                if (hits == 1 || hits % 500 == 0)
                    NSLog(@"[metal] %d precompiled programs used, %d compiled from source", hits, gMisses);
                return LC_RETURN_NEW(lib);
            }
            if (__sync_add_and_fetch(&gLoadFailures, 1) <= 5)
                NSLog(@"[metal] precompiled program %016llx failed to load: %@", key, loadError);
        }
    }
    uint64_t t0 = mach_absolute_time();
    void *lib = gOrigNewLibraryWithSource(self, _cmd, source, options, error);
    double ms = LCMetalMs(t0);
    int32_t misses = __sync_add_and_fetch(&gMisses, 1);
    if (ms > 250 || misses <= 3)
        NSLog(@"[metal] compiled from source (not precompiled): %zu bytes in %.0f ms%s", length, ms, lib ? "" : " FAILED");
    return lib;
}

static void LCMetalLoadPack(void)
{
    NSString *path = [[[NSBundle mainBundle] bundlePath] stringByAppendingPathComponent:@"Data/Raw/lcmetallib.pack"];
    int fd = open([path fileSystemRepresentation], O_RDONLY);
    if (fd < 0) { NSLog(@"[metal] no precompiled pack at %@", path); return; }
    struct stat st;
    if (fstat(fd, &st) != 0 || st.st_size < 16) { close(fd); return; }
    void *p = mmap(NULL, (size_t)st.st_size, PROT_READ, MAP_PRIVATE, fd, 0);
    close(fd);
    if (p == MAP_FAILED) { NSLog(@"[metal] mmap failed"); return; }
    const uint8_t *b = (const uint8_t *)p;
    uint32_t version, count;
    memcpy(&version, b + 4, 4);
    memcpy(&count, b + 8, 4);
    if (memcmp(b, "LCML", 4) != 0 || version != 1 || 16 + (uint64_t)count * sizeof(LCMetalEntry) > (uint64_t)st.st_size)
    {
        NSLog(@"[metal] bad precompiled pack");
        munmap(p, (size_t)st.st_size);
        return;
    }
    gPack = b;
    gIndex = (const LCMetalEntry *)(b + 16);
    gCount = count;
    NSLog(@"[metal] %u precompiled programs (%lld MB)", count, (long long)(st.st_size >> 20));
}

__attribute__((constructor)) static void LCMetalInstall(void)
{
    @autoreleasepool
    {
        LCMetalLoadPack();
        id<MTLDevice> device = MTLCreateSystemDefaultDevice();
        if (!device) return;
        SEL sel = @selector(newLibraryWithSource:options:error:);
        Method m = class_getInstanceMethod([(NSObject *)device class], sel);
        if (!m) { NSLog(@"[metal] newLibraryWithSource not found"); return; }
        gOrigNewLibraryWithSource = (void *(*)(id, SEL, NSString *, MTLCompileOptions *, NSError **))method_getImplementation(m);
        method_setImplementation(m, (IMP)LCNewLibraryWithSource);
        NSLog(@"[metal] hooked %s", class_getName([(NSObject *)device class]));
#if !__has_feature(objc_arc)
        [device release];
#endif
    }
}
