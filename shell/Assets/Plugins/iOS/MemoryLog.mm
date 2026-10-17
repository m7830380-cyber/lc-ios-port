#import <Foundation/Foundation.h>
#import <UIKit/UIKit.h>
#import <mach/mach.h>
#import <os/proc.h>

// Writes the app's memory footprint to the native log every 5 s, so a run that dies
// without a crash report still shows how close it got to the jetsam limit.
static void LCMemoryFootprint(const char *tag)
{
    task_vm_info_data_t info;
    mach_msg_type_number_t count = TASK_VM_INFO_COUNT;
    if (task_info(mach_task_self(), TASK_VM_INFO, (task_info_t)&info, &count) != KERN_SUCCESS) return;
    NSLog(@"[mem] %s footprint=%llu MB limit_left=%llu MB",
          tag, (unsigned long long)(info.phys_footprint >> 20), (unsigned long long)(os_proc_available_memory() >> 20));
}

__attribute__((constructor)) static void LCMemoryLogInstall(void)
{
    [[NSNotificationCenter defaultCenter] addObserverForName:UIApplicationDidReceiveMemoryWarningNotification
                                                      object:nil
                                                       queue:nil
                                                  usingBlock:^(NSNotification *note) { LCMemoryFootprint("memory warning"); }];

    dispatch_queue_t queue = dispatch_queue_create("lc.memlog", DISPATCH_QUEUE_SERIAL);
    dispatch_source_t timer = dispatch_source_create(DISPATCH_SOURCE_TYPE_TIMER, 0, 0, queue);
    dispatch_source_set_timer(timer, dispatch_time(DISPATCH_TIME_NOW, 0), 5 * NSEC_PER_SEC, NSEC_PER_SEC);
    dispatch_source_set_event_handler(timer, ^{ LCMemoryFootprint("periodic"); });
    dispatch_resume(timer);
}
