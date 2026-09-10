/* 使用 source-lock.json 锁定的 v0.41.0 头文件编译，独立验证托管测试的 ABI 预期。
 * cl /std:c17 /c /I <mpv-source>/include NativeAbiProbe.c
 */
#include <stddef.h>
#include <mpv/client.h>
#include <mpv/render.h>
#include <mpv/render_gl.h>

_Static_assert(sizeof(void *) == 8, "x64");
_Static_assert(MPV_CLIENT_API_VERSION == MPV_MAKE_VERSION(2, 5), "client API");
_Static_assert(sizeof(mpv_event) == 24, "mpv_event");
_Static_assert(offsetof(mpv_event, reply_userdata) == 8, "reply_userdata");
_Static_assert(offsetof(mpv_event, data) == 16, "event data");
_Static_assert(sizeof(mpv_event_property) == 24, "mpv_event_property");
_Static_assert(offsetof(mpv_event_property, data) == 16, "property data");
_Static_assert(sizeof(mpv_node) == 16, "mpv_node");
_Static_assert(offsetof(mpv_node, format) == 8, "node format");
_Static_assert(sizeof(mpv_node_list) == 24, "mpv_node_list");
_Static_assert(sizeof(mpv_event_end_file) == 32, "mpv_event_end_file");
_Static_assert(sizeof(mpv_render_param) == 16, "mpv_render_param");
_Static_assert(sizeof(mpv_opengl_init_params) == 16, "mpv_opengl_init_params");
_Static_assert(sizeof(mpv_opengl_fbo) == 16, "mpv_opengl_fbo");
_Static_assert(MPV_FORMAT_NODE == 6 && MPV_FORMAT_NODE_MAP == 8, "formats");
_Static_assert(MPV_EVENT_PROPERTY_CHANGE == 22 && MPV_EVENT_COMMAND_REPLY == 5, "events");
_Static_assert(MPV_RENDER_PARAM_API_TYPE == 1 && MPV_RENDER_PARAM_OPENGL_INIT_PARAMS == 2, "init params");
_Static_assert(MPV_RENDER_PARAM_OPENGL_FBO == 3 && MPV_RENDER_PARAM_FLIP_Y == 4, "render params");
_Static_assert(MPV_RENDER_PARAM_BLOCK_FOR_TARGET_TIME == 12 && MPV_RENDER_UPDATE_FRAME == 1, "render flags");
