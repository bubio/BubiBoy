# ROM Smoke Testing

The repository does not include commercial ROMs, BIOS files, or proprietary assets. Local ROM collections can be used for private verification without committing them.

Run the smoke runner against a local ROM directory:

```sh
dotnet run --project tools/BubiBoy.RomSmoke/BubiBoy.RomSmoke.fsproj -- /Volumes/CrucialX6/roms/GB --steps 2000
```

The runner recursively scans `.gb` and `.gbc` files, skips files containing `[BIOS]` by default, loads cartridge headers, creates an emulator session, and runs a bounded number of CPU steps.

Output statuses:

- `STEP_LIMIT`: execution reached the requested step count.
- `HALTED`: CPU entered the halted state.
- `UNSUPPORTED_OPCODE`: execution stopped at an opcode not implemented yet.
- `LOAD_ERROR`: ROM loading or cartridge setup failed.

Useful options:

```sh
--steps N
--max N
--include-bios
--fail-on-load-error
```

`UNSUPPORTED_OPCODE` is expected during Phase 2 while the CPU instruction set is incomplete. Treat it as actionable coverage data rather than a smoke-run infrastructure failure.
