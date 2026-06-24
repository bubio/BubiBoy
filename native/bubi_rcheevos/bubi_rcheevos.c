#include "bubi_rcheevos.h"

#include "rc_client.h"
#include "rc_consoles.h"
#include "rc_version.h"

#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#define BUBI_RA_MEMORY_SIZE 0x34000U
#define BUBI_RA_MEMORY_PAGE_SIZE 64U
#define BUBI_RA_MEMORY_PAGE_COUNT \
  (BUBI_RA_MEMORY_SIZE / BUBI_RA_MEMORY_PAGE_SIZE)

#if defined(__APPLE__)
#include <Security/Security.h>
#endif

typedef struct bubi_ra_pending_request {
  struct bubi_ra_pending_request* next;
  rc_client_server_callback_t callback;
  void* callback_data;
} bubi_ra_pending_request;

struct bubi_ra_client {
  rc_client_t* native;
  bubi_ra_read_memory_callback read_memory;
  bubi_ra_server_request_callback server_request;
  bubi_ra_event_callback event_callback;
  bubi_ra_log_callback log_callback;
  bubi_ra_operation_callback operation_callback;
  rc_client_async_handle_t* operation_handle;
  void* userdata;
  bubi_ra_pending_request* requests;
  uint8_t memory_cache[BUBI_RA_MEMORY_SIZE];
  uint8_t memory_cache_valid[BUBI_RA_MEMORY_PAGE_COUNT];
};

static void bubi_copy_string(char* destination, size_t size, const char* source) {
  if (!destination || size == 0)
    return;

  if (!source)
    source = "";

  snprintf(destination, size, "%s", source);
}

static bubi_ra_client* bubi_context(rc_client_t* client) {
  return (bubi_ra_client*)rc_client_get_userdata(client);
}

static uint32_t bubi_read_memory(uint32_t address, uint8_t* buffer, uint32_t count,
                                 rc_client_t* client) {
  bubi_ra_client* context = bubi_context(client);
  uint32_t copied = 0;

  if (!context || !context->read_memory || !buffer ||
      address >= BUBI_RA_MEMORY_SIZE)
    return 0;

  if (count > BUBI_RA_MEMORY_SIZE - address)
    count = BUBI_RA_MEMORY_SIZE - address;

  while (copied < count) {
    const uint32_t current_address = address + copied;
    const uint32_t page = current_address / BUBI_RA_MEMORY_PAGE_SIZE;
    const uint32_t page_start = page * BUBI_RA_MEMORY_PAGE_SIZE;
    const uint32_t page_offset = current_address - page_start;
    uint32_t chunk = BUBI_RA_MEMORY_PAGE_SIZE - page_offset;

    if (!context->memory_cache_valid[page]) {
      const uint32_t fetched = context->read_memory(
          context->userdata, page_start, &context->memory_cache[page_start],
          BUBI_RA_MEMORY_PAGE_SIZE);
      if (fetched != BUBI_RA_MEMORY_PAGE_SIZE)
        return copied;
      context->memory_cache_valid[page] = 1;
    }

    if (chunk > count - copied)
      chunk = count - copied;
    memcpy(buffer + copied, &context->memory_cache[current_address], chunk);
    copied += chunk;
  }

  return copied;
}

static void bubi_server_request(const rc_api_request_t* request,
                                rc_client_server_callback_t callback,
                                void* callback_data, rc_client_t* client) {
  bubi_ra_client* context = bubi_context(client);
  bubi_ra_pending_request* pending;

  if (!context || !context->server_request) {
    rc_api_server_response_t response = {0};
    response.http_status_code = RC_API_SERVER_RESPONSE_CLIENT_ERROR;
    callback(&response, callback_data);
    return;
  }

  pending = (bubi_ra_pending_request*)calloc(1, sizeof(*pending));
  if (!pending) {
    rc_api_server_response_t response = {0};
    response.http_status_code = RC_API_SERVER_RESPONSE_CLIENT_ERROR;
    callback(&response, callback_data);
    return;
  }

  pending->callback = callback;
  pending->callback_data = callback_data;
  pending->next = context->requests;
  context->requests = pending;
  context->server_request(context->userdata, (uintptr_t)pending, request->url,
                          request->post_data, request->content_type);
}

static void bubi_log_message(const char* message, const rc_client_t* client) {
  bubi_ra_client* context = (bubi_ra_client*)rc_client_get_userdata(client);
  if (context && context->log_callback)
    context->log_callback(context->userdata, RC_CLIENT_LOG_LEVEL_INFO, message);
}

static void bubi_event(const rc_client_event_t* event, rc_client_t* client) {
  bubi_ra_client* context = bubi_context(client);
  uint32_t related_id = 0;
  const char* title = "";
  const char* description = "";
  const char* measured_progress = "";
  float measured_percent = 0.0f;
  char image_url[1024] = {0};

  if (!context || !context->event_callback)
    return;

  if (event->achievement) {
    related_id = event->achievement->id;
    title = event->achievement->title;
    description = event->achievement->description;
    measured_progress = event->achievement->measured_progress;
    measured_percent = event->achievement->measured_percent;
    rc_client_achievement_get_image_url(event->achievement,
                                        event->type == RC_CLIENT_EVENT_ACHIEVEMENT_TRIGGERED
                                            ? RC_CLIENT_ACHIEVEMENT_STATE_UNLOCKED
                                            : RC_CLIENT_ACHIEVEMENT_STATE_ACTIVE,
                                        image_url, sizeof(image_url));
  } else if (event->leaderboard) {
    related_id = event->leaderboard->id;
    title = event->leaderboard->title;
    description = event->leaderboard->description;
  } else if (event->server_error) {
    related_id = event->server_error->related_id;
    title = event->server_error->api;
    description = event->server_error->error_message;
  } else if (event->subset) {
    related_id = event->subset->id;
    title = event->subset->title;
  }

  context->event_callback(context->userdata, event->type, related_id,
                          title ? title : "", description ? description : "",
                          image_url, measured_progress ? measured_progress : "",
                          measured_percent);
}

static void bubi_operation_complete(int result, const char* error_message,
                                    rc_client_t* client, void* userdata) {
  bubi_ra_client* context = (bubi_ra_client*)userdata;
  bubi_ra_operation_callback callback = context->operation_callback;
  context->operation_callback = NULL;
  context->operation_handle = NULL;
  if (callback)
    callback(context->userdata, result, error_message ? error_message : "");
}

bubi_ra_client* bubi_ra_create(bubi_ra_read_memory_callback read_memory,
                               bubi_ra_server_request_callback server_request,
                               bubi_ra_event_callback event_callback,
                               bubi_ra_log_callback log_callback,
                               void* userdata) {
  bubi_ra_client* context = (bubi_ra_client*)calloc(1, sizeof(*context));
  if (!context)
    return NULL;

  context->read_memory = read_memory;
  context->server_request = server_request;
  context->event_callback = event_callback;
  context->log_callback = log_callback;
  context->userdata = userdata;
  context->native = rc_client_create(bubi_read_memory, bubi_server_request);
  if (!context->native) {
    free(context);
    return NULL;
  }

  rc_client_set_userdata(context->native, context);
  rc_client_set_event_handler(context->native, bubi_event);
  rc_client_enable_logging(context->native, RC_CLIENT_LOG_LEVEL_WARN,
                           bubi_log_message);
  return context;
}

void bubi_ra_destroy(bubi_ra_client* client) {
  if (!client)
    return;
  bubi_ra_cancel_operation(client);
  bubi_ra_abort_server_requests(client);
  rc_client_destroy(client->native);
  free(client);
}

uint32_t bubi_ra_version(void) { return rc_version(); }
const char* bubi_ra_version_string(void) { return rc_version_string(); }

size_t bubi_ra_user_agent(bubi_ra_client* client, char* buffer, size_t size) {
  return client ? rc_client_get_user_agent_clause(client->native, buffer, size) : 0;
}

void bubi_ra_complete_server_request(bubi_ra_client* client, uintptr_t request_id,
                                     int http_status, const uint8_t* body,
                                     size_t body_size) {
  bubi_ra_pending_request* pending = (bubi_ra_pending_request*)request_id;
  bubi_ra_pending_request** link;
  rc_api_server_response_t response = {0};

  if (!client || !pending)
    return;

  link = &client->requests;
  while (*link && *link != pending)
    link = &(*link)->next;
  if (!*link)
    return;

  *link = pending->next;
  response.body = (const char*)body;
  response.body_length = body_size;
  response.http_status_code = http_status;
  pending->callback(&response, pending->callback_data);
  free(pending);
}

void bubi_ra_abort_server_requests(bubi_ra_client* client) {
  while (client && client->requests) {
    bubi_ra_pending_request* pending = client->requests;
    bubi_ra_complete_server_request(client, (uintptr_t)pending,
                                    RC_API_SERVER_RESPONSE_CLIENT_ERROR, NULL, 0);
  }
}

void bubi_ra_cancel_operation(bubi_ra_client* client) {
  if (!client || !client->operation_handle)
    return;
  rc_client_abort_async(client->native, client->operation_handle);
  client->operation_handle = NULL;
  client->operation_callback = NULL;
}

void bubi_ra_login_password(bubi_ra_client* client, const char* username,
                            const char* password,
                            bubi_ra_operation_callback callback) {
  if (!client || client->operation_callback)
    return;
  client->operation_callback = callback;
  client->operation_handle = rc_client_begin_login_with_password(
      client->native, username, password, bubi_operation_complete, client);
}

void bubi_ra_login_token(bubi_ra_client* client, const char* username,
                         const char* token,
                         bubi_ra_operation_callback callback) {
  if (!client || client->operation_callback)
    return;
  client->operation_callback = callback;
  client->operation_handle = rc_client_begin_login_with_token(
      client->native, username, token, bubi_operation_complete, client);
}

void bubi_ra_logout(bubi_ra_client* client) {
  if (client)
    rc_client_logout(client->native);
}

int bubi_ra_get_user(bubi_ra_client* client, char* username, size_t username_size,
                     char* display_name, size_t display_name_size, char* token,
                     size_t token_size, uint32_t* score,
                     uint32_t* softcore_score) {
  const rc_client_user_t* user = client ? rc_client_get_user_info(client->native) : NULL;
  if (!user)
    return 0;
  bubi_copy_string(username, username_size, user->username);
  bubi_copy_string(display_name, display_name_size, user->display_name);
  bubi_copy_string(token, token_size, user->token);
  if (score) *score = user->score;
  if (softcore_score) *softcore_score = user->score_softcore;
  return 1;
}

void bubi_ra_load_game(bubi_ra_client* client, uint32_t console_id,
                       const uint8_t* rom, size_t rom_size,
                       bubi_ra_operation_callback callback) {
  if (!client || client->operation_callback)
    return;
  client->operation_callback = callback;
  client->operation_handle = rc_client_begin_identify_and_load_game(
      client->native, console_id, NULL, rom, rom_size, bubi_operation_complete,
      client);
}

void bubi_ra_unload_game(bubi_ra_client* client) {
  if (client)
    rc_client_unload_game(client->native);
}

int bubi_ra_get_game(bubi_ra_client* client, uint32_t* game_id, char* title,
                     size_t title_size, char* hash, size_t hash_size,
                     char* image_url, size_t image_url_size) {
  const rc_client_game_t* game = client ? rc_client_get_game_info(client->native) : NULL;
  if (!game || game->id == 0)
    return 0;
  if (game_id) *game_id = game->id;
  bubi_copy_string(title, title_size, game->title);
  bubi_copy_string(hash, hash_size, game->hash);
  if (image_url && image_url_size)
    rc_client_game_get_image_url(game, image_url, image_url_size);
  return 1;
}

int bubi_ra_get_rich_presence(bubi_ra_client* client, char* message,
                              size_t message_size) {
  if (message && message_size)
    message[0] = '\0';
  if (!client || !message || message_size == 0 ||
      !rc_client_has_rich_presence(client->native))
    return 0;
  memset(client->memory_cache_valid, 0, sizeof(client->memory_cache_valid));
  rc_client_get_rich_presence_message(client->native, message, message_size);
  return message[0] != '\0';
}

void bubi_ra_enumerate_achievements(bubi_ra_client* client,
                                    bubi_ra_achievement_callback callback) {
  rc_client_achievement_list_t* list;
  uint32_t bucket_index;
  if (!client || !callback)
    return;
  list = rc_client_create_achievement_list(client->native,
                                           RC_CLIENT_ACHIEVEMENT_CATEGORY_CORE,
                                           RC_CLIENT_ACHIEVEMENT_LIST_GROUPING_PROGRESS);
  if (!list)
    return;
  for (bucket_index = 0; bucket_index < list->num_buckets; ++bucket_index) {
    const rc_client_achievement_bucket_t* bucket = &list->buckets[bucket_index];
    uint32_t achievement_index;
    for (achievement_index = 0; achievement_index < bucket->num_achievements;
         ++achievement_index) {
      const rc_client_achievement_t* achievement = bucket->achievements[achievement_index];
      char image_url[1024] = {0};
      rc_client_achievement_get_image_url(achievement, achievement->state,
                                          image_url, sizeof(image_url));
      callback(client->userdata, bucket->bucket_type, bucket->label,
               achievement->id, achievement->title, achievement->description,
               achievement->points, achievement->measured_progress,
               achievement->measured_percent,
               rc_client_get_hardcore_enabled(client->native)
                   ? achievement->rarity_hardcore
                   : achievement->rarity,
               achievement->state, achievement->unlocked, image_url);
    }
  }
  rc_client_destroy_achievement_list(list);
}

void bubi_ra_do_frame(bubi_ra_client* client) {
  if (client) {
    memset(client->memory_cache_valid, 0, sizeof(client->memory_cache_valid));
    rc_client_do_frame(client->native);
  }
}
void bubi_ra_idle(bubi_ra_client* client) { if (client) rc_client_idle(client->native); }
void bubi_ra_set_hardcore_enabled(bubi_ra_client* client, int enabled) {
  if (client)
    rc_client_set_hardcore_enabled(client->native, enabled);
}
int bubi_ra_get_hardcore_enabled(bubi_ra_client* client) {
  return client ? rc_client_get_hardcore_enabled(client->native) : 0;
}
int bubi_ra_can_pause(bubi_ra_client* client, uint32_t* frames_remaining) {
  if (!client) {
    if (frames_remaining)
      *frames_remaining = 0;
    return 1;
  }
  return rc_client_can_pause(client->native, frames_remaining);
}
void bubi_ra_reset(bubi_ra_client* client) { if (client) rc_client_reset(client->native); }
size_t bubi_ra_progress_size(bubi_ra_client* client) { return client ? rc_client_progress_size(client->native) : 0; }
int bubi_ra_serialize_progress(bubi_ra_client* client, uint8_t* buffer, size_t size) {
  return client ? rc_client_serialize_progress_sized(client->native, buffer, size) : -1;
}
int bubi_ra_deserialize_progress(bubi_ra_client* client, const uint8_t* buffer, size_t size) {
  return client ? rc_client_deserialize_progress_sized(client->native, buffer, size) : -1;
}

int bubi_ra_keychain_store(const char* service, const char* account,
                           const char* secret) {
#if defined(__APPLE__)
  SecKeychainItemRef item = NULL;
  OSStatus status = SecKeychainFindGenericPassword(
      NULL, (UInt32)strlen(service), service, (UInt32)strlen(account), account,
      NULL, NULL, &item);
  if (status == errSecSuccess) {
    status = SecKeychainItemModifyAttributesAndData(
        item, NULL, (UInt32)strlen(secret), secret);
    CFRelease(item);
  } else if (status == errSecItemNotFound) {
    status = SecKeychainAddGenericPassword(
        NULL, (UInt32)strlen(service), service, (UInt32)strlen(account), account,
        (UInt32)strlen(secret), secret, NULL);
  }
  return (int)status;
#else
  (void)service; (void)account; (void)secret;
  return -1;
#endif
}

int bubi_ra_keychain_load(const char* service, const char* account,
                          char* secret, size_t secret_size) {
#if defined(__APPLE__)
  void* data = NULL;
  UInt32 length = 0;
  OSStatus status = SecKeychainFindGenericPassword(
      NULL, (UInt32)strlen(service), service, (UInt32)strlen(account), account,
      &length, &data, NULL);
  if (status == errSecSuccess) {
    if (!secret || secret_size == 0 || length >= secret_size) {
      SecKeychainItemFreeContent(NULL, data);
      return (int)errSecBufferTooSmall;
    }
    memcpy(secret, data, length);
    secret[length] = '\0';
    SecKeychainItemFreeContent(NULL, data);
  }
  return (int)status;
#else
  (void)service; (void)account; (void)secret; (void)secret_size;
  return -1;
#endif
}

int bubi_ra_keychain_delete(const char* service, const char* account) {
#if defined(__APPLE__)
  SecKeychainItemRef item = NULL;
  OSStatus status = SecKeychainFindGenericPassword(
      NULL, (UInt32)strlen(service), service, (UInt32)strlen(account), account,
      NULL, NULL, &item);
  if (status == errSecSuccess) {
    status = SecKeychainItemDelete(item);
    CFRelease(item);
  }
  return (int)status;
#else
  (void)service; (void)account;
  return -1;
#endif
}
