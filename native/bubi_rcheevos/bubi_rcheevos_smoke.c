#include "bubi_rcheevos.h"

int main(void) {
  bubi_ra_client* client;
  uint32_t frames_remaining = 0;

  if (bubi_ra_version() != 12003000U)
    return 1;

  client = bubi_ra_create(NULL, NULL, NULL, NULL, NULL);
  if (!client)
    return 2;

  (void)bubi_ra_can_pause(client, &frames_remaining);

  bubi_ra_destroy(client);
  return 0;
}
