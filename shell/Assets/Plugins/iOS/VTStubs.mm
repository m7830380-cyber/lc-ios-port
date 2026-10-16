// Virtual texturing is not used by Lethal Company. Unity still emits registration calls for these internal
// calls (RegisterAllStrippedInternalCalls) but the native module is absent from the iOS player, so the link fails.
// No-op definitions satisfy the linker; nothing in the game calls them.
#include <Foundation/Foundation.h>

void Register_UnityEngine_Rendering_VirtualTexturing_Debugging_get_debugTilesEnabled() {}
void Register_UnityEngine_Rendering_VirtualTexturing_Debugging_get_resolvingEnabled() {}
void Register_UnityEngine_Rendering_VirtualTexturing_Debugging_set_debugTilesEnabled() {}
void Register_UnityEngine_Rendering_VirtualTexturing_Debugging_set_resolvingEnabled() {}
void Register_UnityEngine_Rendering_VirtualTexturing_Procedural_Binding_BindGlobally() {}
void Register_UnityEngine_Rendering_VirtualTexturing_Procedural_Binding_BindToMaterial() {}
void Register_UnityEngine_Rendering_VirtualTexturing_Procedural_Binding_PopRequests() {}
void Register_UnityEngine_Rendering_VirtualTexturing_Procedural_GetGPUCacheSettings() {}
void Register_UnityEngine_Rendering_VirtualTexturing_Procedural_SetGPUCacheSettings() {}
void Register_UnityEngine_Rendering_VirtualTexturing_Streaming_EnableMipPreloading() {}
void Register_UnityEngine_Rendering_VirtualTexturing_Streaming_GetGPUCacheSettings() {}
void Register_UnityEngine_Rendering_VirtualTexturing_Streaming_GetTextureStackSize() {}
void Register_UnityEngine_Rendering_VirtualTexturing_Streaming_RequestRegion_Injected() {}
void Register_UnityEngine_Rendering_VirtualTexturing_Streaming_SetGPUCacheSettings() {}
void Register_UnityEngine_Rendering_VirtualTexturing_System_SetDebugFlagInteger() {}
