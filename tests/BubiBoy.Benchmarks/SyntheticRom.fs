namespace BubiBoy.Benchmarks

/// Builds a small, fully synthetic Game Boy ROM in memory so the benchmarks have a
/// deterministic, license-safe workload (no commercial ROM is ever bundled). The ROM
/// is a 32 KiB ROM-only cartridge whose entry point runs a tight loop that exercises
/// the ALU, WRAM writes, and lets the timer/LCD/APU advance through Bus.tick.
module SyntheticRom =
    [<Literal>]
    let private RomBytes = 32 * 1024

    // A self-contained busy loop starting at the cartridge entry point (0x0100).
    // Chosen opcodes are all widely supported and keep HL inside WRAM (INC L only
    // touches the low byte) so the program never wanders into side-effecting I/O.
    //
    //   0x100: LD HL, 0xC000   21 00 C0   point HL at WRAM
    //   0x103: LD A, B         78
    //   0x104: ADD A, C        81
    //   0x105: ADD A, D        82
    //   0x106: SUB E           93
    //   0x107: LD (HL), A      77         write to WRAM
    //   0x108: INC L           2C         stay within 0xC0xx
    //   0x109: INC B           04
    //   0x10A: DEC C           0D
    //   0x10B: RRCA            0F
    //   0x10C: JP 0x0100       C3 00 01
    let private program =
        [| 0x21uy; 0x00uy; 0xC0uy
           0x78uy
           0x81uy
           0x82uy
           0x93uy
           0x77uy
           0x2Cuy
           0x04uy
           0x0Duy
           0x0Fuy
           0xC3uy; 0x00uy; 0x01uy |]

    /// Produces the synthetic ROM image as a fresh byte array.
    let build () : byte[] =
        let rom = Array.zeroCreate<byte> RomBytes

        // Cartridge header (only the fields CartridgeMemory.create inspects matter).
        // Title "BENCH" at 0x0134.
        let title = "BENCH"
        title |> Seq.iteri (fun i c -> rom[0x0134 + i] <- byte c)
        rom[0x0143] <- 0x00uy // DMG only
        rom[0x0146] <- 0x00uy // no SGB
        rom[0x0147] <- 0x00uy // cartridge type: ROM only
        rom[0x0148] <- 0x00uy // ROM size code 0 -> 32 KiB
        rom[0x0149] <- 0x00uy // no cartridge RAM
        rom[0x014A] <- 0x01uy // destination

        // Program at the entry point.
        System.Array.Copy(program, 0, rom, 0x0100, program.Length)
        rom
