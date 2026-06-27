#include "bubi_rcheevos.h"

static void event_callback(void* userdata, uint32_t event_type,
                           uint32_t related_id, const char* title,
                           const char* description, const char* image_url,
                           const char* measured_progress,
                           float measured_percent, const char* value,
                           const char* best_score, uint32_t rank,
                           uint32_t total_entries) {
  (void)userdata;
  (void)event_type;
  (void)related_id;
  (void)title;
  (void)description;
  (void)image_url;
  (void)measured_progress;
  (void)measured_percent;
  (void)value;
  (void)best_score;
  (void)rank;
  (void)total_entries;
}

static void scoreboard_entry_callback(void* userdata, const char* username,
                                      uint32_t rank, const char* score) {
  (void)userdata;
  (void)username;
  (void)rank;
  (void)score;
}

static void leaderboard_callback(void* userdata, uint8_t bucket,
                                 const char* bucket_label, uint32_t id,
                                 const char* title, const char* description,
                                 const char* tracker_value, uint8_t state,
                                 uint8_t format, uint8_t lower_is_better) {
  (void)userdata;
  (void)bucket;
  (void)bucket_label;
  (void)id;
  (void)title;
  (void)description;
  (void)tracker_value;
  (void)state;
  (void)format;
  (void)lower_is_better;
}

static void leaderboard_entry_callback(void* userdata, uint32_t leaderboard_id,
                                       const char* username, uint32_t rank,
                                       const char* score) {
  (void)userdata;
  (void)leaderboard_id;
  (void)username;
  (void)rank;
  (void)score;
}

static void leaderboard_entries_callback(void* userdata, uint32_t leaderboard_id,
                                         int result,
                                         const char* error_message,
                                         uint32_t total_entries,
                                         int32_t user_index) {
  (void)userdata;
  (void)leaderboard_id;
  (void)result;
  (void)error_message;
  (void)total_entries;
  (void)user_index;
}

int main(void) {
  bubi_ra_client* client;
  uint32_t frames_remaining = 0;
  char rich_presence[256];

  if (bubi_ra_version() != 12003000U)
    return 1;

  client = bubi_ra_create(NULL, NULL, event_callback,
                          scoreboard_entry_callback, NULL, NULL);
  if (!client)
    return 2;

  bubi_ra_set_hardcore_enabled(client, 1);
  if (!bubi_ra_get_hardcore_enabled(client))
    return 3;
  bubi_ra_set_hardcore_enabled(client, 0);
  (void)bubi_ra_can_pause(client, &frames_remaining);
  (void)bubi_ra_get_rich_presence(client, rich_presence,
                                  sizeof(rich_presence));
  bubi_ra_enumerate_leaderboards(client, leaderboard_callback);
  bubi_ra_fetch_leaderboard_entries(client, 1, 1, 1,
                                    leaderboard_entry_callback,
                                    leaderboard_entries_callback);

  bubi_ra_destroy(client);
  return 0;
}
