# ROM Smoke Testing

The repository does not include commercial ROMs, BIOS files, or proprietary assets. Local ROM collections can be used for private verification without committing them.

Run the smoke runner against a local ROM directory:

```sh
dotnet run --project tools/BubiBoy.RomSmoke/BubiBoy.RomSmoke.fsproj -- /Volumes/CrucialX6/roms/GB --steps 2000
```

The runner recursively scans `.gb` and `.gbc` files, skips files containing `[BIOS]` by default, loads cartridge headers, creates an emulator session, and runs a bounded number of CPU steps.
macOS AppleDouble metadata files such as `._game.gb` are ignored because they can appear in shared ROM folders on Windows but are not cartridge data.

Output statuses:

- `STEP_LIMIT`: execution reached the requested step count.
- `HALTED`: CPU entered the halted state.
- `UNSUPPORTED_OPCODE`: execution stopped at an opcode not implemented yet.
- `BAD_STACK_POINTER`: diagnostic mode stopped after `SP` moved below WRAM.
- `BAD_PROGRAM_COUNTER`: diagnostic mode stopped after `PC` entered a suspicious non-code region.
- `LOAD_ERROR`: ROM loading or cartridge setup failed.

Useful options:

```sh
--steps N
--max N
--name TEXT
--trace-tail N
--stop-on-bad-sp
--stop-on-bad-pc
--include-bios
--fail-on-load-error
```

`--name` filters ROMs by file name. `--trace-tail` records the final N executed sessions and prints them
when execution reaches an unsupported opcode or a diagnostic stop. The stack and PC stop options are meant
for investigating late failures in commercial ROM smoke runs without producing huge logs.

`UNSUPPORTED_OPCODE` is expected during Phase 2 while the CPU instruction set is incomplete. Treat it as actionable coverage data rather than a smoke-run infrastructure failure.
