#ifndef BUBI_RCHEEVOS_H
#define BUBI_RCHEEVOS_H

#include <stddef.h>
#include <stdint.h>

#if defined(_WIN32)
#define BUBI_RA_EXPORT __declspec(dllexport)
#else
#define BUBI_RA_EXPORT __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif

typedef struct bubi_ra_client bubi_ra_client;

typedef uint32_t (*bubi_ra_read_memory_callback)(void* userdata, uint32_t address,
                                                 uint8_t* buffer, uint32_t count);
typedef void (*bubi_ra_server_request_callback)(void* userdata, uintptr_t request_id,
                                                const char* url, const char* post_data,
                                                const char* content_type);
typedef void (*bubi_ra_event_callback)(void* userdata, uint32_t event_type, uint32_t related_id,
                                      const char* title, const char* description,
                                      const char* image_url);
typedef void (*bubi_ra_log_callback)(void* userdata, int level, const char* message);
typedef void (*bubi_ra_operation_callback)(void* userdata, int result, const char* error_message);
typedef void (*bubi_ra_achievement_callback)(
    void* userdata, uint8_t bucket, const char* bucket_label, uint32_t id,
    const char* title, const char* description, uint32_t points,
    const char* measured_progress, float measured_percent, float rarity,
    uint8_t state, uint8_t unlocked, const char* image_url);

BUBI_RA_EXPORT bubi_ra_client* bubi_ra_create(
    bubi_ra_read_memory_callback read_memory,
    bubi_ra_server_request_callback server_request,
    bubi_ra_event_callback event_callback,
    bubi_ra_log_callback log_callback,
    void* userdata);
BUBI_RA_EXPORT void bubi_ra_destroy(bubi_ra_client* client);
BUBI_RA_EXPORT uint32_t bubi_ra_version(void);
BUBI_RA_EXPORT const char* bubi_ra_version_string(void);
BUBI_RA_EXPORT size_t bubi_ra_user_agent(bubi_ra_client* client, char* buffer, size_t size);

BUBI_RA_EXPORT void bubi_ra_complete_server_request(bubi_ra_client* client,
                                                    uintptr_t request_id,
                                                    int http_status,
                                                    const uint8_t* body,
                                                    size_t body_size);
BUBI_RA_EXPORT void bubi_ra_abort_server_requests(bubi_ra_client* client);
BUBI_RA_EXPORT void bubi_ra_cancel_operation(bubi_ra_client* client);

BUBI_RA_EXPORT void bubi_ra_login_password(bubi_ra_client* client, const char* username,
                                           const char* password,
                                           bubi_ra_operation_callback callback);
BUBI_RA_EXPORT void bubi_ra_login_token(bubi_ra_client* client, const char* username,
                                        const char* token,
                                        bubi_ra_operation_callback callback);
BUBI_RA_EXPORT void bubi_ra_logout(bubi_ra_client* client);
BUBI_RA_EXPORT int bubi_ra_get_user(bubi_ra_client* client, char* username, size_t username_size,
                                    char* display_name, size_t display_name_size,
                                    char* token, size_t token_size, uint32_t* score,
                                    uint32_t* softcore_score);

BUBI_RA_EXPORT void bubi_ra_load_game(bubi_ra_client* client, uint32_t console_id,
                                      const uint8_t* rom, size_t rom_size,
                                      bubi_ra_operation_callback callback);
BUBI_RA_EXPORT void bubi_ra_unload_game(bubi_ra_client* client);
BUBI_RA_EXPORT int bubi_ra_get_game(bubi_ra_client* client, uint32_t* game_id,
                                    char* title, size_t title_size, char* hash,
                                    size_t hash_size, char* image_url,
                                    size_t image_url_size);
BUBI_RA_EXPORT void bubi_ra_enumerate_achievements(bubi_ra_client* client,
                                                   bubi_ra_achievement_callback callback);

BUBI_RA_EXPORT void bubi_ra_do_frame(bubi_ra_client* client);
BUBI_RA_EXPORT void bubi_ra_idle(bubi_ra_client* client);
BUBI_RA_EXPORT void bubi_ra_reset(bubi_ra_client* client);
BUBI_RA_EXPORT size_t bubi_ra_progress_size(bubi_ra_client* client);
BUBI_RA_EXPORT int bubi_ra_serialize_progress(bubi_ra_client* client, uint8_t* buffer,
                                              size_t size);
BUBI_RA_EXPORT int bubi_ra_deserialize_progress(bubi_ra_client* client,
                                                const uint8_t* buffer, size_t size);
BUBI_RA_EXPORT int bubi_ra_keychain_store(const char* service, const char* account,
                                          const char* secret);
BUBI_RA_EXPORT int bubi_ra_keychain_load(const char* service, const char* account,
                                         char* secret, size_t secret_size);
BUBI_RA_EXPORT int bubi_ra_keychain_delete(const char* service, const char* account);

#ifdef __cplusplus
}
#endif

#endif
