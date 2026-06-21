#include "bubi_rcheevos.h"

int main(void) {
  bubi_ra_client* client;

  if (bubi_ra_version() != 12003000U)
    return 1;

  client = bubi_ra_create(NULL, NULL, NULL, NULL, NULL);
  if (!client)
    return 2;

  bubi_ra_destroy(client);
  return 0;
}
