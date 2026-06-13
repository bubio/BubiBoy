# Mooneye Test ROM Manifest

Source distribution:

- Project: Mooneye Test Suite
- Version: `mts-20240926-1737-443f6e1`
- Download: https://gekkio.fi/files/mooneye-test-suite/mts-20240926-1737-443f6e1/
- License: MIT; see `LICENSE`
- Target: DMG acceptance behavior

The following ROMs run in the normal cross-platform test suite. SHA-256 values are
from the unmodified upstream distribution.

| ROM | SHA-256 |
| --- | --- |
| `acceptance/add_sp_e_timing.gb` | `bf736cb8fc06949ba2b16d68ab831e9e6a726a8521ff25ea4257d7cb3499839e` |
| `acceptance/bits/reg_f.gb` | `4b193e887ee3ac82b38b796729e1503e9a78da3e1140f8bd5600d0884f2e2627` |
| `acceptance/call_cc_timing.gb` | `2ef47505ed501128071b490174dccb6904ee6037f88723ed98a23382eee7b4d1` |
| `acceptance/call_timing.gb` | `249f524368b8b9bbaa7e05b36f29a5210137d61d59889b596ea505d42e6b749b` |
| `acceptance/div_timing.gb` | `382a9cd42a60ef0a3f03ef834d476a32907f1b3a42b40de5bd3d9705ae9a9734` |
| `acceptance/ei_sequence.gb` | `dcd7f37e8fe7d8eb38cab6732a5826e0bb0278fd1e1d9e297c28d205da1b69e1` |
| `acceptance/ei_timing.gb` | `e5fa88f83727e79912f2c69b91b8e3c1351c0b50ac26203a60c7a6c21e825dbc` |
| `acceptance/halt_ime0_ei.gb` | `0768fd3e698047f5ec2631b5a830558f0ac14bd9cb0ef0ebcc409b00d69adb4d` |
| `acceptance/halt_ime0_nointr_timing.gb` | `40a1e614d77a881672b7c20420aed6b121ad9d3977d71a08fd9bb92ee8f010cf` |
| `acceptance/halt_ime1_timing.gb` | `09d9be4ebdd7645a6b208f18b1354f4b75420ae567dcf27ac404ed6f934d2efa` |
| `acceptance/if_ie_registers.gb` | `d055b2b4c44902cf827296a06b17cea4f2c84f6b7d540c777cb1d1049ef35e61` |
| `acceptance/instr/daa.gb` | `1498d92d70592a07a2493ef764609916616f0b023b21408189e277201e6c14c1` |
| `acceptance/intr_timing.gb` | `a795a190830104a4a231935d3bd16e64a1088bb79084f80bf7d9946ca93d873d` |
| `acceptance/jp_cc_timing.gb` | `8747fa6f969c8ac153a79b37abd2abac5ea98dbe142d5e77e074adec6e9fd481` |
| `acceptance/jp_timing.gb` | `514ba4b96355f5074b63feb159a06af85f4fbea412a2d49599c19ea3781712e0` |
| `acceptance/ld_hl_sp_e_timing.gb` | `fe22571b9aee1681b81b31055b3773574ba11eb838550fd86e0e773cb0f4240e` |
| `acceptance/pop_timing.gb` | `4b658ab238b46319f24fa890faf9f337f03ce8769c05be6540742c9aaaf5773a` |
| `acceptance/push_timing.gb` | `f764ee5053f4d1f84896ec3ff8761e30beded493d204566e3e5b20af736179f9` |
| `acceptance/rapid_di_ei.gb` | `4bcfbee2dcdca7895afff7947742ec942166aeb4899f07995863149ef360f7f0` |
| `acceptance/ret_cc_timing.gb` | `eb95fe488131f625c656631b0188484f3126d80944584128dc514aaf95111fb6` |
| `acceptance/ret_timing.gb` | `cfb13c7aa11ff299402530c021fe93bd6f51769433757fd32e46985eb1296206` |
| `acceptance/rst_timing.gb` | `8ba26360bad21b451f36551863e60831f98e3de1308ae6bf2aee7a73973c99c2` |
| `acceptance/timer/div_write.gb` | `2be1e4da6fa24b9123d2a8bae47dd0d6f5e97e1855186c0c0f49e6d213eebfff` |
| `acceptance/timer/tim00.gb` | `2193036c1628efd9ba86e5729292ef716d6ff3178cfa2abb9797709cd40252e8` |
| `acceptance/timer/tim01.gb` | `b6f5043eae7fd2b2c3dc098ff16f664c8eb5699523616d84274669cf90c17fe7` |
| `acceptance/timer/tim10.gb` | `fe3b0b292341d5ff26c9db3f6c9f3ba8a3d6e8b63977c61767a457962bd1faed` |
| `acceptance/timer/tim11.gb` | `624fd3ad3ede2790095162cfa212e488825072a0ecc287ff0e88da6c5d7040f1` |
| `acceptance/timer/tima_reload.gb` | `1ca70c725bd1e027b07d3058839bd140eccddd9f4ca41305c4f8ab3acaff8a98` |
| `acceptance/timer/tima_write_reloading.gb` | `7d9a6d5ada792596621f8bfdf257112887b2dd01d98e0f91a253afd6e05d0540` |
| `acceptance/timer/tma_write_reloading.gb` | `e48ff98d4f363b92e92bdabe86253fcf63f648964e3a61e73d52aedcba3e5ab2` |

Do not replace or add binaries without updating this manifest and
`docs/reference-provenance.md`.
