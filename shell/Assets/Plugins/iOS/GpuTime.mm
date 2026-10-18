// Where the frame time goes, measured outside Unity: how long the GPU is busy per second (from the completion times
// of every Metal command buffer) and the CPU time of each thread (Unity's main and render threads among them).
// Logged every 5 s as [gpu]/[threads]; LCGpuBusyMs() lets the C# side measure a span itself.
#import <Foundation/Foundation.h>
#import <Metal/Metal.h>
#include <objc/runtime.h>
#include <mach/mach.h>
#include <pthread.h>
#include <atomic>
#include <os/lock.h>
#include <stdio.h>
#include <string.h>

static void (*gOrigCommit)(id, SEL);
static std::atomic<double> gBusyMs;            // GPU busy time, overlapping command buffers counted once
static std::atomic<long> gBuffers;
static double gLastEnd;                   // only touched under gLock
static os_unfair_lock gLock = OS_UNFAIR_LOCK_INIT;
static dispatch_source_t gTimer;

extern "C" double LCGpuBusyMs(void) { return gBusyMs.load(); }

static void LCCommit(id self, SEL _cmd)
{
    [(id<MTLCommandBuffer>)self addCompletedHandler:^(id<MTLCommandBuffer> b) {
        double start = b.GPUStartTime, end = b.GPUEndTime;
        if (end <= start) return;
        os_unfair_lock_lock(&gLock);
        if (start < gLastEnd) start = gLastEnd;
        if (end > start)
        {
            gBusyMs.store(gBusyMs.load() + (end - start) * 1000.0);
            gLastEnd = end;
        }
        os_unfair_lock_unlock(&gLock);
        gBuffers++;
    }];
    gOrigCommit(self, _cmd);
}

static void LCHookCommandBuffers(void)
{
    id<MTLDevice> device = MTLCreateSystemDefaultDevice();
    id<MTLCommandQueue> queue = [device newCommandQueue];
    Class cls = [[queue commandBuffer] class];
    Method m = class_getInstanceMethod(cls, @selector(commit));
    if (!m) { NSLog(@"[gpu] no commit method on %s", class_getName(cls)); return; }
    gOrigCommit = (void (*)(id, SEL))method_setImplementation(m, (IMP)LCCommit);
    NSLog(@"[gpu] timing command buffers of class %s", class_getName(cls));
}

struct LCThreadTime { uint64_t id; double cpu; char name[32]; };
static LCThreadTime gPrev[128];
static int gPrevCount;

static void LCLogThreads(double seconds)
{
    thread_act_array_t threads;
    mach_msg_type_number_t count;
    if (task_threads(mach_task_self(), &threads, &count) != KERN_SUCCESS) return;
    LCThreadTime now[128];
    int n = 0;
    for (mach_msg_type_number_t i = 0; i < count && n < 128; i++)
    {
        thread_basic_info_data_t basic; mach_msg_type_number_t bc = THREAD_BASIC_INFO_COUNT;
        thread_identifier_info_data_t ident; mach_msg_type_number_t ic = THREAD_IDENTIFIER_INFO_COUNT;
        if (thread_info(threads[i], THREAD_BASIC_INFO, (thread_info_t)&basic, &bc) == KERN_SUCCESS &&
            thread_info(threads[i], THREAD_IDENTIFIER_INFO, (thread_info_t)&ident, &ic) == KERN_SUCCESS)
        {
            LCThreadTime &t = now[n++];
            t.id = ident.thread_id;
            t.cpu = basic.user_time.seconds + basic.user_time.microseconds / 1e6 + basic.system_time.seconds + basic.system_time.microseconds / 1e6;
            t.name[0] = 0;
            pthread_t p = pthread_from_mach_thread_np(threads[i]);
            if (p) pthread_getname_np(p, t.name, sizeof(t.name));
            if (!t.name[0]) snprintf(t.name, sizeof(t.name), "#%llu", (unsigned long long)t.id);
        }
        mach_port_deallocate(mach_task_self(), threads[i]);
    }
    vm_deallocate(mach_task_self(), (vm_address_t)threads, count * sizeof(thread_act_t));

    // Busiest threads since the last report, in percent of one core.
    int order[128]; double use[128];
    for (int i = 0; i < n; i++)
    {
        use[i] = 0; order[i] = i;
        for (int j = 0; j < gPrevCount; j++)
            if (gPrev[j].id == now[i].id) { use[i] = (now[i].cpu - gPrev[j].cpu) / seconds * 100; break; }
    }
    for (int i = 0; i < n; i++)
        for (int j = i + 1; j < n; j++)
            if (use[order[j]] > use[order[i]]) { int t = order[i]; order[i] = order[j]; order[j] = t; }
    NSMutableString *s = [NSMutableString stringWithString:@"[threads]"];
    for (int i = 0; i < n && i < 8; i++)
        if (use[order[i]] >= 1) [s appendFormat:@" %s=%.0f%%", now[order[i]].name, use[order[i]]];
    NSLog(@"%@", s);
    memcpy(gPrev, now, n * sizeof(LCThreadTime));
    gPrevCount = n;
}

__attribute__((constructor)) static void LCGpuTimeInstall(void)
{
    LCHookCommandBuffers();
    dispatch_queue_t queue = dispatch_queue_create("lc.gputime", DISPATCH_QUEUE_SERIAL);
    gTimer = dispatch_source_create(DISPATCH_SOURCE_TYPE_TIMER, 0, 0, queue);
    const double every = 5;
    dispatch_source_set_timer(gTimer, dispatch_time(DISPATCH_TIME_NOW, (int64_t)(every * NSEC_PER_SEC)), (uint64_t)(every * NSEC_PER_SEC), NSEC_PER_SEC / 10);
    dispatch_source_set_event_handler(gTimer, ^{
        static double lastBusy; static long lastBuffers;
        double busy = gBusyMs.load(); long buffers = gBuffers.load();
        NSLog(@"[gpu] busy=%.0f ms/s (%.0f%%) command buffers=%.0f/s",
              (busy - lastBusy) / every, (busy - lastBusy) / every / 10, (buffers - lastBuffers) / every);
        lastBusy = busy; lastBuffers = buffers;
        LCLogThreads(every);
    });
    dispatch_resume(gTimer);
}
