#include "bubi_rcheevos.h"

static void event_callback(void* userdata, uint32_t event_type,
                           uint32_t related_id, const char* title,
                           const char* description, const char* image_url,
                           const char* measured_progress,
                           float measured_percent) {
  (void)userdata;
  (void)event_type;
  (void)related_id;
  (void)title;
  (void)description;
  (void)image_url;
  (void)measured_progress;
  (void)measured_percent;
}

int main(void) {
  bubi_ra_client* client;
  uint32_t frames_remaining = 0;

  if (bubi_ra_version() != 12003000U)
    return 1;

  client = bubi_ra_create(NULL, NULL, event_callback, NULL, NULL);
  if (!client)
    return 2;

  (void)bubi_ra_can_pause(client, &frames_remaining);

  bubi_ra_destroy(client);
  return 0;
}
