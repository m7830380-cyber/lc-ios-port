#import <Foundation/Foundation.h>
#import <UIKit/UIKit.h>
#import <mach/mach.h>
#import <os/proc.h>
#include <dlfcn.h>
#include <errno.h>
#include <string.h>
#include <unistd.h>

// Writes the app's memory footprint to the native log every 5 s, so a run that dies
// without a crash report still shows how close it got to the jetsam limit.
// Also raises the app's own jetsam limit: with increased-memory-limit alone iOS caps it near 4.1 GB,
// and the game passes that while loading the ship after hosting. Needs com.apple.private.memorystatus (TrollStore).

#define LC_CMD_SET_JETSAM_TASK_LIMIT 6
#define LC_CMD_SET_MEMLIMIT_PROPERTIES 7
#define LC_MEMLIMIT_ATTR_FATAL 0x1

struct LCMemlimitProperties { int32_t active; uint32_t activeAttr; int32_t inactive; uint32_t inactiveAttr; };
typedef int (*LCMemorystatusControl)(uint32_t command, int32_t pid, uint32_t flags, void *buffer, size_t size);

static dispatch_source_t gTimer;   // must stay referenced, or the timer is freed and never fires
static int32_t gWantedLimitMB;

static uint64_t LCMemoryLeftMB(void) { return os_proc_available_memory() >> 20; }

static void LCMemoryFootprint(const char *tag)
{
    task_vm_info_data_t info;
    mach_msg_type_number_t count = TASK_VM_INFO_COUNT;
    if (task_info(mach_task_self(), TASK_VM_INFO, (task_info_t)&info, &count) != KERN_SUCCESS) return;
    NSLog(@"[mem] %s footprint=%llu MB limit_left=%llu MB",
          tag, (unsigned long long)(info.phys_footprint >> 20), (unsigned long long)LCMemoryLeftMB());
}

// Returns 0 on success, else errno.
static int LCRaiseLimit(int32_t mb)
{
    static LCMemorystatusControl control;
    if (!control) control = (LCMemorystatusControl)dlsym(RTLD_DEFAULT, "memorystatus_control");
    if (!control) return ENOSYS;
    LCMemlimitProperties p = { mb, LC_MEMLIMIT_ATTR_FATAL, mb, LC_MEMLIMIT_ATTR_FATAL };
    if (control(LC_CMD_SET_MEMLIMIT_PROPERTIES, getpid(), 0, &p, sizeof(p)) == 0) return 0;
    int err = errno;
    if (control(LC_CMD_SET_JETSAM_TASK_LIMIT, getpid(), (uint32_t)mb, NULL, 0) == 0) return 0;
    return err;
}

static void LCRaiseLimitAtLaunch(void)
{
    int32_t physicalMB = (int32_t)([NSProcessInfo processInfo].physicalMemory >> 20);
    uint64_t before = LCMemoryLeftMB();
    // Leave about 512 MB for iOS itself; step down if the kernel refuses a value.
    for (int32_t mb = physicalMB - 512; mb >= 4096; mb -= 256)
    {
        int err = LCRaiseLimit(mb);
        if (err == 0)
        {
            gWantedLimitMB = mb;
            NSLog(@"[mem] jetsam limit raised to %d MB (ram=%d MB, limit_left %llu -> %llu MB)",
                  mb, physicalMB, (unsigned long long)before, (unsigned long long)LCMemoryLeftMB());
            return;
        }
        if (err == EPERM || err == ENOSYS)
        {
            NSLog(@"[mem] cannot raise jetsam limit: %s", strerror(err));
            return;
        }
    }
    NSLog(@"[mem] kernel refused every jetsam limit above 4096 MB");
}

__attribute__((constructor)) static void LCMemoryLogInstall(void)
{
    LCRaiseLimitAtLaunch();

    [[NSNotificationCenter defaultCenter] addObserverForName:UIApplicationDidReceiveMemoryWarningNotification
                                                      object:nil
                                                       queue:nil
                                                  usingBlock:^(NSNotification *note) {
        LCMemoryFootprint("memory warning");
        // iOS drops the limit back to ~4.1 GB around memory warnings; restore it at once.
        if (gWantedLimitMB) LCRaiseLimit(gWantedLimitMB);
    }];

    dispatch_queue_t queue = dispatch_queue_create("lc.memlog", DISPATCH_QUEUE_SERIAL);
    gTimer = dispatch_source_create(DISPATCH_SOURCE_TYPE_TIMER, 0, 0, queue);
    dispatch_source_set_timer(gTimer, dispatch_time(DISPATCH_TIME_NOW, 0), NSEC_PER_SEC, NSEC_PER_SEC / 10);
    dispatch_source_set_event_handler(gTimer, ^{
        static int tick;
        // iOS resets the limit now and then (seen around memory warnings); put it back every second.
        if (gWantedLimitMB) LCRaiseLimit(gWantedLimitMB);
        if (tick++ % 5 == 0) LCMemoryFootprint("periodic");
    });
    dispatch_resume(gTimer);
}
