module BubiBoy.Core.Tests.CpuTests

open BubiBoy.Core
open Xunit

let private makeRomWithProgram (program: byte[]) =
    let rom = Array.zeroCreate<byte> (2 * 16 * 1024)
    rom[0x0147] <- 0x00uy
    rom[0x0148] <- 0x00uy
    rom[0x0149] <- 0x00uy

    program
    |> Array.iteri (fun index value -> rom[0x0100 + index] <- value)

    rom

let private makeBus program =
    match makeRomWithProgram program |> CartridgeMemory.create with
    | Ok cartridge -> Bus.create cartridge
    | Error message -> failwith message

[<Fact>]
let ``NOP advances PC and consumes four cycles`` () =
    let bus = makeBus [| 0x00uy |]
    let result = Cpu.step Cpu.initialState bus

    Assert.Equal(0x0101us, result.Cpu.Registers.PC)
    Assert.Equal(4, result.Cycles)

[<Fact>]
let ``LD SP d16 loads immediate little endian value`` () =
    let bus = makeBus [| 0x31uy; 0x34uy; 0x12uy |]
    let result = Cpu.step Cpu.initialState bus

    Assert.Equal(0x1234us, result.Cpu.Registers.SP)
    Assert.Equal(0x0103us, result.Cpu.Registers.PC)
    Assert.Equal(12, result.Cycles)

[<Fact>]
let ``LD a16 SP stores stack pointer little endian`` () =
    let bus = makeBus [| 0x08uy; 0x00uy; 0xC0uy |]
    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with SP = 0xDFFEus } }

    let result = Cpu.step cpu bus

    Assert.Equal(0xFEuy, Bus.readByte 0xC000us result.Bus)
    Assert.Equal(0xDFuy, Bus.readByte 0xC001us result.Bus)
    Assert.Equal(0x0103us, result.Cpu.Registers.PC)
    Assert.Equal(20, result.Cycles)

[<Fact>]
let ``INC SP increments stack pointer without changing flags`` () =
    let bus = makeBus [| 0x33uy |]
    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with SP = 0xDFFEus; F = Cpu.CarryFlag } }

    let result = Cpu.step cpu bus

    Assert.Equal(0xDFFFus, result.Cpu.Registers.SP)
    Assert.Equal(Cpu.CarryFlag, result.Cpu.Registers.F)
    Assert.Equal(0x0101us, result.Cpu.Registers.PC)
    Assert.Equal(8, result.Cycles)

[<Fact>]
let ``LD BC d16 loads immediate little endian value`` () =
    let bus = makeBus [| 0x01uy; 0x78uy; 0x56uy |]
    let result = Cpu.step Cpu.initialState bus

    Assert.Equal(0x56uy, result.Cpu.Registers.B)
    Assert.Equal(0x78uy, result.Cpu.Registers.C)
    Assert.Equal(0x0103us, result.Cpu.Registers.PC)
    Assert.Equal(12, result.Cycles)

[<Fact>]
let ``LD BC A stores accumulator through bus`` () =
    let bus = makeBus [| 0x02uy |]
    let cpu =
        { Cpu.initialState with
            Registers =
                { Cpu.initialRegisters with
                    A = 0x5Euy
                    B = 0xC0uy
                    C = 0x80uy } }

    let result = Cpu.step cpu bus

    Assert.Equal(0x5Euy, Bus.readByte 0xC080us result.Bus)
    Assert.Equal(0x0101us, result.Cpu.Registers.PC)
    Assert.Equal(8, result.Cycles)

[<Fact>]
let ``INC BC increments BC without changing flags`` () =
    let bus = makeBus [| 0x03uy |]
    let cpu =
        { Cpu.initialState with
            Registers =
                { Cpu.initialRegisters with
                    B = 0x12uy
                    C = 0xFFuy
                    F = Cpu.CarryFlag } }

    let result = Cpu.step cpu bus

    Assert.Equal(0x13uy, result.Cpu.Registers.B)
    Assert.Equal(0x00uy, result.Cpu.Registers.C)
    Assert.Equal(Cpu.CarryFlag, result.Cpu.Registers.F)
    Assert.Equal(0x0101us, result.Cpu.Registers.PC)
    Assert.Equal(8, result.Cycles)

[<Fact>]
let ``INC B updates B and flags preserving carry`` () =
    let bus = makeBus [| 0x04uy |]
    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with B = 0x0Fuy; F = Cpu.CarryFlag } }

    let result = Cpu.step cpu bus

    Assert.Equal(0x10uy, result.Cpu.Registers.B)
    Assert.Equal(Cpu.HalfCarryFlag ||| Cpu.CarryFlag, result.Cpu.Registers.F)
    Assert.Equal(0x0101us, result.Cpu.Registers.PC)
    Assert.Equal(4, result.Cycles)

[<Fact>]
let ``RLCA rotates A left and moves bit seven to carry and bit zero`` () =
    let bus = makeBus [| 0x07uy |]
    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with A = 0x80uy; F = Cpu.ZeroFlag } }

    let result = Cpu.step cpu bus

    Assert.Equal(0x01uy, result.Cpu.Registers.A)
    Assert.Equal(Cpu.CarryFlag, result.Cpu.Registers.F)
    Assert.Equal(0x0101us, result.Cpu.Registers.PC)
    Assert.Equal(4, result.Cycles)

[<Fact>]
let ``RLA rotates A left through carry and clears zero`` () =
    let bus = makeBus [| 0x17uy |]
    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with A = 0x80uy; F = Cpu.ZeroFlag ||| Cpu.CarryFlag } }

    let result = Cpu.step cpu bus

    Assert.Equal(0x01uy, result.Cpu.Registers.A)
    Assert.Equal(Cpu.CarryFlag, result.Cpu.Registers.F)
    Assert.Equal(0x0101us, result.Cpu.Registers.PC)
    Assert.Equal(4, result.Cycles)

[<Fact>]
let ``RRCA rotates A right and moves bit zero to carry and bit seven`` () =
    let bus = makeBus [| 0x0Fuy |]
    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with A = 0x01uy; F = Cpu.ZeroFlag } }

    let result = Cpu.step cpu bus

    Assert.Equal(0x80uy, result.Cpu.Registers.A)
    Assert.Equal(Cpu.CarryFlag, result.Cpu.Registers.F)
    Assert.Equal(0x0101us, result.Cpu.Registers.PC)
    Assert.Equal(4, result.Cycles)

[<Fact>]
let ``RRA rotates A right through carry`` () =
    let bus = makeBus [| 0x1Fuy |]
    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with A = 0x01uy; F = Cpu.CarryFlag ||| Cpu.ZeroFlag } }

    let result = Cpu.step cpu bus

    Assert.Equal(0x80uy, result.Cpu.Registers.A)
    Assert.Equal(Cpu.CarryFlag, result.Cpu.Registers.F)
    Assert.Equal(0x0101us, result.Cpu.Registers.PC)
    Assert.Equal(4, result.Cycles)

[<Fact>]
let ``LD DE d16 loads immediate little endian value`` () =
    let bus = makeBus [| 0x11uy; 0xCDuy; 0xABuy |]
    let result = Cpu.step Cpu.initialState bus

    Assert.Equal(0xABuy, result.Cpu.Registers.D)
    Assert.Equal(0xCDuy, result.Cpu.Registers.E)
    Assert.Equal(0x0103us, result.Cpu.Registers.PC)
    Assert.Equal(12, result.Cycles)

[<Fact>]
let ``STOP enters low-power wait and advances over padding byte`` () =
    let bus = makeBus [| 0x10uy; 0x00uy |]
    let result = Cpu.step Cpu.initialState bus

    Assert.True(result.Cpu.Halted)
    Assert.Equal(0x0102us, result.Cpu.Registers.PC)
    Assert.Equal(4, result.Cycles)

[<Fact>]
let ``LD A DE reads through bus`` () =
    let bus =
        makeBus [| 0x1Auy |]
        |> Bus.writeByte 0xC123us 0xA5uy

    let cpu =
        { Cpu.initialState with
            Registers =
                { Cpu.initialRegisters with
                    D = 0xC1uy
                    E = 0x23uy } }

    let result = Cpu.step cpu bus

    Assert.Equal(0xA5uy, result.Cpu.Registers.A)
    Assert.Equal(0x0101us, result.Cpu.Registers.PC)
    Assert.Equal(8, result.Cycles)

[<Fact>]
let ``LD DE A stores accumulator through bus`` () =
    let bus = makeBus [| 0x12uy |]
    let cpu =
        { Cpu.initialState with
            Registers =
                { Cpu.initialRegisters with
                    A = 0xB7uy
                    D = 0xC1uy
                    E = 0x23uy } }

    let result = Cpu.step cpu bus

    Assert.Equal(0xB7uy, Bus.readByte 0xC123us result.Bus)
    Assert.Equal(0x0101us, result.Cpu.Registers.PC)
    Assert.Equal(8, result.Cycles)

[<Fact>]
let ``LD A BC reads through bus`` () =
    let bus =
        makeBus [| 0x0Auy |]
        |> Bus.writeByte 0xC456us 0x6Auy

    let cpu =
        { Cpu.initialState with
            Registers =
                { Cpu.initialRegisters with
                    B = 0xC4uy
                    C = 0x56uy } }

    let result = Cpu.step cpu bus

    Assert.Equal(0x6Auy, result.Cpu.Registers.A)
    Assert.Equal(0x0101us, result.Cpu.Registers.PC)
    Assert.Equal(8, result.Cycles)

[<Fact>]
let ``INC DE increments DE without changing flags`` () =
    let bus = makeBus [| 0x13uy |]
    let cpu =
        { Cpu.initialState with
            Registers =
                { Cpu.initialRegisters with
                    D = 0x12uy
                    E = 0xFFuy
                    F = Cpu.CarryFlag } }

    let result = Cpu.step cpu bus

    Assert.Equal(0x13uy, result.Cpu.Registers.D)
    Assert.Equal(0x00uy, result.Cpu.Registers.E)
    Assert.Equal(Cpu.CarryFlag, result.Cpu.Registers.F)
    Assert.Equal(0x0101us, result.Cpu.Registers.PC)
    Assert.Equal(8, result.Cycles)

[<Fact>]
let ``DEC DE decrements DE without changing flags`` () =
    let bus = makeBus [| 0x1Buy |]
    let cpu =
        { Cpu.initialState with
            Registers =
                { Cpu.initialRegisters with
                    D = 0x12uy
                    E = 0x00uy
                    F = Cpu.CarryFlag } }

    let result = Cpu.step cpu bus

    Assert.Equal(0x11uy, result.Cpu.Registers.D)
    Assert.Equal(0xFFuy, result.Cpu.Registers.E)
    Assert.Equal(Cpu.CarryFlag, result.Cpu.Registers.F)
    Assert.Equal(0x0101us, result.Cpu.Registers.PC)
    Assert.Equal(8, result.Cycles)

[<Fact>]
let ``DEC E updates E and flags preserving carry`` () =
    let bus = makeBus [| 0x1Duy |]
    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with E = 0x10uy; F = Cpu.CarryFlag } }

    let result = Cpu.step cpu bus

    Assert.Equal(0x0Fuy, result.Cpu.Registers.E)
    Assert.Equal(Cpu.SubtractFlag ||| Cpu.HalfCarryFlag ||| Cpu.CarryFlag, result.Cpu.Registers.F)
    Assert.Equal(0x0101us, result.Cpu.Registers.PC)
    Assert.Equal(4, result.Cycles)

[<Fact>]
let ``INC E updates E and flags preserving carry`` () =
    let bus = makeBus [| 0x1Cuy |]
    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with E = 0x0Fuy; F = Cpu.CarryFlag } }

    let result = Cpu.step cpu bus

    Assert.Equal(0x10uy, result.Cpu.Registers.E)
    Assert.Equal(Cpu.HalfCarryFlag ||| Cpu.CarryFlag, result.Cpu.Registers.F)
    Assert.Equal(0x0101us, result.Cpu.Registers.PC)
    Assert.Equal(4, result.Cycles)

[<Fact>]
let ``INC D updates D and flags preserving carry`` () =
    let bus = makeBus [| 0x14uy |]
    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with D = 0x0Fuy; F = Cpu.CarryFlag } }

    let result = Cpu.step cpu bus

    Assert.Equal(0x10uy, result.Cpu.Registers.D)
    Assert.Equal(Cpu.HalfCarryFlag ||| Cpu.CarryFlag, result.Cpu.Registers.F)
    Assert.Equal(0x0101us, result.Cpu.Registers.PC)
    Assert.Equal(4, result.Cycles)

[<Fact>]
let ``DEC D updates D and flags preserving carry`` () =
    let bus = makeBus [| 0x15uy |]
    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with D = 0x01uy; F = Cpu.CarryFlag } }

    let result = Cpu.step cpu bus

    Assert.Equal(0x00uy, result.Cpu.Registers.D)
    Assert.Equal(Cpu.ZeroFlag ||| Cpu.SubtractFlag ||| Cpu.CarryFlag, result.Cpu.Registers.F)
    Assert.Equal(0x0101us, result.Cpu.Registers.PC)
    Assert.Equal(4, result.Cycles)

[<Fact>]
let ``DEC A updates accumulator and flags preserving carry`` () =
    let bus = makeBus [| 0x3Duy |]
    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with A = 0x01uy; F = Cpu.CarryFlag } }

    let result = Cpu.step cpu bus

    Assert.Equal(0x00uy, result.Cpu.Registers.A)
    Assert.Equal(Cpu.ZeroFlag ||| Cpu.SubtractFlag ||| Cpu.CarryFlag, result.Cpu.Registers.F)
    Assert.Equal(0x0101us, result.Cpu.Registers.PC)
    Assert.Equal(4, result.Cycles)

[<Fact>]
let ``INC A updates accumulator and flags preserving carry`` () =
    let bus = makeBus [| 0x3Cuy |]
    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with A = 0x0Fuy; F = Cpu.CarryFlag } }

    let result = Cpu.step cpu bus

    Assert.Equal(0x10uy, result.Cpu.Registers.A)
    Assert.Equal(Cpu.HalfCarryFlag ||| Cpu.CarryFlag, result.Cpu.Registers.F)
    Assert.Equal(0x0101us, result.Cpu.Registers.PC)
    Assert.Equal(4, result.Cycles)

[<Fact>]
let ``DEC BC decrements BC without changing flags`` () =
    let bus = makeBus [| 0x0Buy |]
    let cpu =
        { Cpu.initialState with
            Registers =
                { Cpu.initialRegisters with
                    B = 0x12uy
                    C = 0x00uy
                    F = Cpu.CarryFlag } }

    let result = Cpu.step cpu bus

    Assert.Equal(0x11uy, result.Cpu.Registers.B)
    Assert.Equal(0xFFuy, result.Cpu.Registers.C)
    Assert.Equal(Cpu.CarryFlag, result.Cpu.Registers.F)
    Assert.Equal(0x0101us, result.Cpu.Registers.PC)
    Assert.Equal(8, result.Cycles)

[<Fact>]
let ``ADD HL BC adds BC into HL and updates half carry`` () =
    let bus = makeBus [| 0x09uy |]
    let cpu =
        { Cpu.initialState with
            Registers =
                { Cpu.initialRegisters with
                    B = 0x00uy
                    C = 0x01uy
                    H = 0x0Fuy
                    L = 0xFFuy
                    F = Cpu.ZeroFlag ||| Cpu.SubtractFlag ||| Cpu.CarryFlag } }

    let result = Cpu.step cpu bus

    Assert.Equal(0x10uy, result.Cpu.Registers.H)
    Assert.Equal(0x00uy, result.Cpu.Registers.L)
    Assert.Equal(Cpu.ZeroFlag ||| Cpu.HalfCarryFlag, result.Cpu.Registers.F)
    Assert.Equal(0x0101us, result.Cpu.Registers.PC)
    Assert.Equal(8, result.Cycles)

[<Fact>]
let ``ADD HL BC wraps and sets carry`` () =
    let bus = makeBus [| 0x09uy |]
    let cpu =
        { Cpu.initialState with
            Registers =
                { Cpu.initialRegisters with
                    B = 0x00uy
                    C = 0x01uy
                    H = 0xFFuy
                    L = 0xFFuy
                    F = 0uy } }

    let result = Cpu.step cpu bus

    Assert.Equal(0x00uy, result.Cpu.Registers.H)
    Assert.Equal(0x00uy, result.Cpu.Registers.L)
    Assert.Equal(Cpu.HalfCarryFlag ||| Cpu.CarryFlag, result.Cpu.Registers.F)
    Assert.Equal(0x0101us, result.Cpu.Registers.PC)
    Assert.Equal(8, result.Cycles)

[<Theory>]
[<InlineData(0x19uy)>]
[<InlineData(0x29uy)>]
[<InlineData(0x39uy)>]
let ``ADD HL register pair adds selected pair into HL`` opcode =
    let bus = makeBus [| opcode |]
    let cpu =
        { Cpu.initialState with
            Registers =
                { Cpu.initialRegisters with
                    D = 0x00uy
                    E = 0x03uy
                    H = 0x00uy
                    L = 0x03uy
                    SP = 0x0003us
                    F = Cpu.ZeroFlag } }

    let result = Cpu.step cpu bus

    let expected = 0x0006us
    Assert.Equal(byte (expected >>> 8), result.Cpu.Registers.H)
    Assert.Equal(byte expected, result.Cpu.Registers.L)
    Assert.Equal(Cpu.ZeroFlag, result.Cpu.Registers.F)
    Assert.Equal(0x0101us, result.Cpu.Registers.PC)
    Assert.Equal(8, result.Cycles)

[<Fact>]
let ``INC C updates C and flags preserving carry`` () =
    let bus = makeBus [| 0x0Cuy |]
    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with C = 0x0Fuy; F = Cpu.CarryFlag } }

    let result = Cpu.step cpu bus

    Assert.Equal(0x10uy, result.Cpu.Registers.C)
    Assert.Equal(Cpu.HalfCarryFlag ||| Cpu.CarryFlag, result.Cpu.Registers.F)
    Assert.Equal(0x0101us, result.Cpu.Registers.PC)
    Assert.Equal(4, result.Cycles)

[<Fact>]
let ``JP a16 sets PC to immediate target`` () =
    let bus = makeBus [| 0xC3uy; 0x00uy; 0x20uy |]
    let result = Cpu.step Cpu.initialState bus

    Assert.Equal(0x2000us, result.Cpu.Registers.PC)
    Assert.Equal(16, result.Cycles)

[<Fact>]
let ``JP HL sets PC to HL`` () =
    let bus = makeBus [| 0xE9uy |]
    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with H = 0xC1uy; L = 0x23uy } }

    let result = Cpu.step cpu bus

    Assert.Equal(0xC123us, result.Cpu.Registers.PC)
    Assert.Equal(4, result.Cycles)

[<Fact>]
let ``XOR A clears A and sets zero flag`` () =
    let bus = makeBus [| 0xAFuy |]
    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with A = 0x42uy; F = 0x10uy } }
    let result = Cpu.step cpu bus

    Assert.Equal(0uy, result.Cpu.Registers.A)
    Assert.Equal(Cpu.ZeroFlag, result.Cpu.Registers.F)
    Assert.Equal(0x0101us, result.Cpu.Registers.PC)

[<Fact>]
let ``XOR C updates A and clears arithmetic flags`` () =
    let bus = makeBus [| 0xA9uy |]
    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with A = 0xF0uy; C = 0x0Fuy; F = 0xF0uy } }

    let result = Cpu.step cpu bus

    Assert.Equal(0xFFuy, result.Cpu.Registers.A)
    Assert.Equal(0uy, result.Cpu.Registers.F)
    Assert.Equal(0x0101us, result.Cpu.Registers.PC)
    Assert.Equal(4, result.Cycles)

[<Fact>]
let ``XOR B updates A and clears arithmetic flags`` () =
    let bus = makeBus [| 0xA8uy |]
    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with A = 0xF0uy; B = 0x0Fuy; F = 0xF0uy } }

    let result = Cpu.step cpu bus

    Assert.Equal(0xFFuy, result.Cpu.Registers.A)
    Assert.Equal(0uy, result.Cpu.Registers.F)
    Assert.Equal(0x0101us, result.Cpu.Registers.PC)
    Assert.Equal(4, result.Cycles)

[<Fact>]
let ``XOR d8 updates A and sets zero flag when result is zero`` () =
    let bus = makeBus [| 0xEEuy; 0x3Cuy |]
    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with A = 0x3Cuy; F = 0xF0uy } }

    let result = Cpu.step cpu bus

    Assert.Equal(0uy, result.Cpu.Registers.A)
    Assert.Equal(Cpu.ZeroFlag, result.Cpu.Registers.F)
    Assert.Equal(0x0102us, result.Cpu.Registers.PC)
    Assert.Equal(8, result.Cycles)

[<Fact>]
let ``XOR HL updates A from memory`` () =
    let bus =
        makeBus [| 0xAEuy |]
        |> Bus.writeByte 0xC020us 0x0Fuy

    let cpu =
        { Cpu.initialState with
            Registers =
                { Cpu.initialRegisters with
                    A = 0xF0uy
                    H = 0xC0uy
                    L = 0x20uy
                    F = 0xF0uy } }

    let result = Cpu.step cpu bus

    Assert.Equal(0xFFuy, result.Cpu.Registers.A)
    Assert.Equal(0uy, result.Cpu.Registers.F)
    Assert.Equal(0x0101us, result.Cpu.Registers.PC)
    Assert.Equal(8, result.Cycles)

[<Fact>]
let ``LDH a8 A stores A in high IO page`` () =
    let bus = makeBus [| 0xE0uy; 0x50uy |]
    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with A = 0x91uy } }
    let result = Cpu.step cpu bus

    Assert.Equal(0x91uy, Bus.readByte 0xFF50us result.Bus)
    Assert.Equal(0x0102us, result.Cpu.Registers.PC)

[<Fact>]
let ``LD C A stores A in high IO page addressed by C`` () =
    let bus = makeBus [| 0xE2uy |]
    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with A = 0x3Euy; C = 0x80uy } }

    let result = Cpu.step cpu bus

    Assert.Equal(0x3Euy, Bus.readByte 0xFF80us result.Bus)
    Assert.Equal(0x0101us, result.Cpu.Registers.PC)
    Assert.Equal(8, result.Cycles)

[<Fact>]
let ``LD HL d16 and LD HLD A write through bus and decrement HL`` () =
    let bus = makeBus [| 0x21uy; 0x00uy; 0xC0uy; 0x32uy |]
    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with A = 0x77uy } }

    let afterLoad = Cpu.step cpu bus
    let afterStore = Cpu.step afterLoad.Cpu afterLoad.Bus

    Assert.Equal(0xC000us, (uint16 afterLoad.Cpu.Registers.H <<< 8) ||| uint16 afterLoad.Cpu.Registers.L)
    Assert.Equal(0x77uy, Bus.readByte 0xC000us afterStore.Bus)
    Assert.Equal(0xBFFFus, (uint16 afterStore.Cpu.Registers.H <<< 8) ||| uint16 afterStore.Cpu.Registers.L)

[<Fact>]
let ``LD HLI A writes through bus and increments HL`` () =
    let bus = makeBus [| 0x22uy |]
    let cpu =
        { Cpu.initialState with
            Registers =
                { Cpu.initialRegisters with
                    A = 0x44uy
                    H = 0xC0uy
                    L = 0x20uy } }

    let result = Cpu.step cpu bus

    Assert.Equal(0x44uy, Bus.readByte 0xC020us result.Bus)
    Assert.Equal(0xC021us, (uint16 result.Cpu.Registers.H <<< 8) ||| uint16 result.Cpu.Registers.L)
    Assert.Equal(0x0101us, result.Cpu.Registers.PC)
    Assert.Equal(8, result.Cycles)

[<Fact>]
let ``LD A HLI reads through bus and increments HL`` () =
    let bus =
        makeBus [| 0x2Auy |]
        |> Bus.writeByte 0xC020us 0x77uy

    let cpu =
        { Cpu.initialState with
            Registers =
                { Cpu.initialRegisters with
                    A = 0uy
                    H = 0xC0uy
                    L = 0x20uy } }

    let result = Cpu.step cpu bus

    Assert.Equal(0x77uy, result.Cpu.Registers.A)
    Assert.Equal(0xC021us, (uint16 result.Cpu.Registers.H <<< 8) ||| uint16 result.Cpu.Registers.L)
    Assert.Equal(0x0101us, result.Cpu.Registers.PC)
    Assert.Equal(8, result.Cycles)

[<Fact>]
let ``LD A HLD reads through bus and decrements HL`` () =
    let bus =
        makeBus [| 0x3Auy |]
        |> Bus.writeByte 0xC020us 0x3Auy

    let cpu =
        { Cpu.initialState with
            Registers =
                { Cpu.initialRegisters with
                    A = 0uy
                    H = 0xC0uy
                    L = 0x20uy } }

    let result = Cpu.step cpu bus

    Assert.Equal(0x3Auy, result.Cpu.Registers.A)
    Assert.Equal(0xC01Fus, (uint16 result.Cpu.Registers.H <<< 8) ||| uint16 result.Cpu.Registers.L)
    Assert.Equal(0x0101us, result.Cpu.Registers.PC)
    Assert.Equal(8, result.Cycles)

[<Fact>]
let ``INC HL increments HL without changing flags`` () =
    let bus = makeBus [| 0x23uy |]
    let cpu =
        { Cpu.initialState with
            Registers =
                { Cpu.initialRegisters with
                    H = 0x12uy
                    L = 0xFFuy
                    F = Cpu.CarryFlag } }

    let result = Cpu.step cpu bus

    Assert.Equal(0x1300us, (uint16 result.Cpu.Registers.H <<< 8) ||| uint16 result.Cpu.Registers.L)
    Assert.Equal(Cpu.CarryFlag, result.Cpu.Registers.F)
    Assert.Equal(0x0101us, result.Cpu.Registers.PC)
    Assert.Equal(8, result.Cycles)

[<Fact>]
let ``INC H updates H and flags preserving carry`` () =
    let bus = makeBus [| 0x24uy |]
    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with H = 0x0Fuy; F = Cpu.CarryFlag } }

    let result = Cpu.step cpu bus

    Assert.Equal(0x10uy, result.Cpu.Registers.H)
    Assert.Equal(Cpu.HalfCarryFlag ||| Cpu.CarryFlag, result.Cpu.Registers.F)
    Assert.Equal(0x0101us, result.Cpu.Registers.PC)
    Assert.Equal(4, result.Cycles)

[<Fact>]
let ``INC L updates L and flags preserving carry`` () =
    let bus = makeBus [| 0x2Cuy |]
    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with L = 0x0Fuy; F = Cpu.CarryFlag } }

    let result = Cpu.step cpu bus

    Assert.Equal(0x10uy, result.Cpu.Registers.L)
    Assert.Equal(Cpu.HalfCarryFlag ||| Cpu.CarryFlag, result.Cpu.Registers.F)
    Assert.Equal(0x0101us, result.Cpu.Registers.PC)
    Assert.Equal(4, result.Cycles)

[<Fact>]
let ``DEC L updates L and flags preserving carry`` () =
    let bus = makeBus [| 0x2Duy |]
    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with L = 0x10uy; F = Cpu.CarryFlag } }

    let result = Cpu.step cpu bus

    Assert.Equal(0x0Fuy, result.Cpu.Registers.L)
    Assert.Equal(Cpu.SubtractFlag ||| Cpu.HalfCarryFlag ||| Cpu.CarryFlag, result.Cpu.Registers.F)
    Assert.Equal(0x0101us, result.Cpu.Registers.PC)
    Assert.Equal(4, result.Cycles)

[<Fact>]
let ``LD HL d8 stores immediate value through bus`` () =
    let bus = makeBus [| 0x36uy; 0x66uy |]
    let cpu =
        { Cpu.initialState with
            Registers =
                { Cpu.initialRegisters with
                    H = 0xC0uy
                    L = 0x10uy } }

    let result = Cpu.step cpu bus

    Assert.Equal(0x66uy, Bus.readByte 0xC010us result.Bus)
    Assert.Equal(0x0102us, result.Cpu.Registers.PC)
    Assert.Equal(12, result.Cycles)

[<Fact>]
let ``LD HL A stores accumulator through bus`` () =
    let bus = makeBus [| 0x77uy |]
    let cpu =
        { Cpu.initialState with
            Registers =
                { Cpu.initialRegisters with
                    A = 0x91uy
                    H = 0xC0uy
                    L = 0x10uy } }

    let result = Cpu.step cpu bus

    Assert.Equal(0x91uy, Bus.readByte 0xC010us result.Bus)
    Assert.Equal(0x0101us, result.Cpu.Registers.PC)
    Assert.Equal(8, result.Cycles)

[<Fact>]
let ``LD HL C stores C through bus`` () =
    let bus = makeBus [| 0x71uy |]
    let cpu =
        { Cpu.initialState with
            Registers =
                { Cpu.initialRegisters with
                    C = 0x71uy
                    H = 0xC0uy
                    L = 0x10uy } }

    let result = Cpu.step cpu bus

    Assert.Equal(0x71uy, Bus.readByte 0xC010us result.Bus)
    Assert.Equal(0x0101us, result.Cpu.Registers.PC)
    Assert.Equal(8, result.Cycles)

[<Fact>]
let ``LD HL B stores B through bus`` () =
    let bus = makeBus [| 0x70uy |]
    let cpu =
        { Cpu.initialState with
            Registers =
                { Cpu.initialRegisters with
                    B = 0x70uy
                    H = 0xC0uy
                    L = 0x10uy } }

    let result = Cpu.step cpu bus

    Assert.Equal(0x70uy, Bus.readByte 0xC010us result.Bus)
    Assert.Equal(0x0101us, result.Cpu.Registers.PC)
    Assert.Equal(8, result.Cycles)

[<Fact>]
let ``LD HL D stores D through bus`` () =
    let bus = makeBus [| 0x72uy |]
    let cpu =
        { Cpu.initialState with
            Registers =
                { Cpu.initialRegisters with
                    D = 0x72uy
                    H = 0xC0uy
                    L = 0x10uy } }

    let result = Cpu.step cpu bus

    Assert.Equal(0x72uy, Bus.readByte 0xC010us result.Bus)
    Assert.Equal(0x0101us, result.Cpu.Registers.PC)
    Assert.Equal(8, result.Cycles)

[<Fact>]
let ``LD HL E stores E through bus`` () =
    let bus = makeBus [| 0x73uy |]
    let cpu =
        { Cpu.initialState with
            Registers =
                { Cpu.initialRegisters with
                    E = 0x73uy
                    H = 0xC0uy
                    L = 0x10uy } }

    let result = Cpu.step cpu bus

    Assert.Equal(0x73uy, Bus.readByte 0xC010us result.Bus)
    Assert.Equal(0x0101us, result.Cpu.Registers.PC)
    Assert.Equal(8, result.Cycles)

[<Fact>]
let ``LD HL L stores L through bus`` () =
    let bus = makeBus [| 0x75uy |]
    let cpu =
        { Cpu.initialState with
            Registers =
                { Cpu.initialRegisters with
                    H = 0xC0uy
                    L = 0x10uy } }

    let result = Cpu.step cpu bus

    Assert.Equal(0x10uy, Bus.readByte 0xC010us result.Bus)
    Assert.Equal(0x0101us, result.Cpu.Registers.PC)
    Assert.Equal(8, result.Cycles)

[<Fact>]
let ``HALT sets halted state and advances PC`` () =
    let bus = makeBus [| 0x76uy |]
    let result = Cpu.step Cpu.initialState bus

    Assert.True(result.Cpu.Halted)
    Assert.Equal(0x0101us, result.Cpu.Registers.PC)
    Assert.Equal(4, result.Cycles)

[<Fact>]
let ``INC HL memory increments memory and updates flags`` () =
    let bus =
        makeBus [| 0x34uy |]
        |> Bus.writeByte 0xC010us 0x0Fuy

    let cpu =
        { Cpu.initialState with
            Registers =
                { Cpu.initialRegisters with
                    H = 0xC0uy
                    L = 0x10uy
                    F = Cpu.CarryFlag } }

    let result = Cpu.step cpu bus

    Assert.Equal(0x10uy, Bus.readByte 0xC010us result.Bus)
    Assert.Equal(Cpu.HalfCarryFlag ||| Cpu.CarryFlag, result.Cpu.Registers.F)
    Assert.Equal(0x0101us, result.Cpu.Registers.PC)
    Assert.Equal(12, result.Cycles)

[<Fact>]
let ``DEC HL memory decrements memory and updates flags`` () =
    let bus =
        makeBus [| 0x35uy |]
        |> Bus.writeByte 0xC010us 0x10uy

    let cpu =
        { Cpu.initialState with
            Registers =
                { Cpu.initialRegisters with
                    H = 0xC0uy
                    L = 0x10uy
                    F = Cpu.CarryFlag } }

    let result = Cpu.step cpu bus

    Assert.Equal(0x0Fuy, Bus.readByte 0xC010us result.Bus)
    Assert.Equal(Cpu.SubtractFlag ||| Cpu.HalfCarryFlag ||| Cpu.CarryFlag, result.Cpu.Registers.F)
    Assert.Equal(0x0101us, result.Cpu.Registers.PC)
    Assert.Equal(12, result.Cycles)

[<Fact>]
let ``JR NZ branches only when zero flag is clear`` () =
    let bus = makeBus [| 0x20uy; 0x02uy; 0x00uy; 0x00uy |]
    let clearZero = { Cpu.initialState with Registers = { Cpu.initialRegisters with F = 0uy } }
    let setZero = { Cpu.initialState with Registers = { Cpu.initialRegisters with F = Cpu.ZeroFlag } }

    let branched = Cpu.step clearZero bus
    let notBranched = Cpu.step setZero bus

    Assert.Equal(0x0104us, branched.Cpu.Registers.PC)
    Assert.Equal(12, branched.Cycles)
    Assert.Equal(0x0102us, notBranched.Cpu.Registers.PC)
    Assert.Equal(8, notBranched.Cycles)

[<Fact>]
let ``JR Z branches only when zero flag is set`` () =
    let bus = makeBus [| 0x28uy; 0x02uy; 0x00uy; 0x00uy |]
    let setZero = { Cpu.initialState with Registers = { Cpu.initialRegisters with F = Cpu.ZeroFlag } }
    let clearZero = { Cpu.initialState with Registers = { Cpu.initialRegisters with F = 0uy } }

    let branched = Cpu.step setZero bus
    let notBranched = Cpu.step clearZero bus

    Assert.Equal(0x0104us, branched.Cpu.Registers.PC)
    Assert.Equal(12, branched.Cycles)
    Assert.Equal(0x0102us, notBranched.Cpu.Registers.PC)
    Assert.Equal(8, notBranched.Cycles)

[<Fact>]
let ``JR C branches only when carry flag is set`` () =
    let bus = makeBus [| 0x38uy; 0xFEuy |]
    let setCarry = { Cpu.initialState with Registers = { Cpu.initialRegisters with F = Cpu.CarryFlag } }
    let clearCarry = { Cpu.initialState with Registers = { Cpu.initialRegisters with F = 0uy } }

    let branched = Cpu.step setCarry bus
    let notBranched = Cpu.step clearCarry bus

    Assert.Equal(0x0100us, branched.Cpu.Registers.PC)
    Assert.Equal(12, branched.Cycles)
    Assert.Equal(0x0102us, notBranched.Cpu.Registers.PC)
    Assert.Equal(8, notBranched.Cycles)

[<Fact>]
let ``JR NC branches only when carry flag is clear`` () =
    let bus = makeBus [| 0x30uy; 0xFEuy |]
    let clearCarry = { Cpu.initialState with Registers = { Cpu.initialRegisters with F = 0uy } }
    let setCarry = { Cpu.initialState with Registers = { Cpu.initialRegisters with F = Cpu.CarryFlag } }

    let branched = Cpu.step clearCarry bus
    let notBranched = Cpu.step setCarry bus

    Assert.Equal(0x0100us, branched.Cpu.Registers.PC)
    Assert.Equal(12, branched.Cycles)
    Assert.Equal(0x0102us, notBranched.Cpu.Registers.PC)
    Assert.Equal(8, notBranched.Cycles)

[<Fact>]
let ``JP Z jumps only when zero flag is set`` () =
    let bus = makeBus [| 0xCAuy; 0x00uy; 0x20uy |]
    let setZero = { Cpu.initialState with Registers = { Cpu.initialRegisters with F = Cpu.ZeroFlag } }
    let clearZero = { Cpu.initialState with Registers = { Cpu.initialRegisters with F = 0uy } }

    let jumped = Cpu.step setZero bus
    let notJumped = Cpu.step clearZero bus

    Assert.Equal(0x2000us, jumped.Cpu.Registers.PC)
    Assert.Equal(16, jumped.Cycles)
    Assert.Equal(0x0103us, notJumped.Cpu.Registers.PC)
    Assert.Equal(12, notJumped.Cycles)

[<Fact>]
let ``JP NZ jumps only when zero flag is clear`` () =
    let bus = makeBus [| 0xC2uy; 0x00uy; 0x20uy |]
    let clearZero = { Cpu.initialState with Registers = { Cpu.initialRegisters with F = 0uy } }
    let setZero = { Cpu.initialState with Registers = { Cpu.initialRegisters with F = Cpu.ZeroFlag } }

    let jumped = Cpu.step clearZero bus
    let notJumped = Cpu.step setZero bus

    Assert.Equal(0x2000us, jumped.Cpu.Registers.PC)
    Assert.Equal(16, jumped.Cycles)
    Assert.Equal(0x0103us, notJumped.Cpu.Registers.PC)
    Assert.Equal(12, notJumped.Cycles)

[<Fact>]
let ``JP NC jumps only when carry flag is clear`` () =
    let bus = makeBus [| 0xD2uy; 0x00uy; 0x20uy |]
    let clearCarry = { Cpu.initialState with Registers = { Cpu.initialRegisters with F = 0uy } }
    let setCarry = { Cpu.initialState with Registers = { Cpu.initialRegisters with F = Cpu.CarryFlag } }

    let jumped = Cpu.step clearCarry bus
    let notJumped = Cpu.step setCarry bus

    Assert.Equal(0x2000us, jumped.Cpu.Registers.PC)
    Assert.Equal(16, jumped.Cycles)
    Assert.Equal(0x0103us, notJumped.Cpu.Registers.PC)
    Assert.Equal(12, notJumped.Cycles)

[<Fact>]
let ``JP C jumps only when carry flag is set`` () =
    let bus = makeBus [| 0xDAuy; 0x00uy; 0x20uy |]
    let setCarry = { Cpu.initialState with Registers = { Cpu.initialRegisters with F = Cpu.CarryFlag } }
    let clearCarry = { Cpu.initialState with Registers = { Cpu.initialRegisters with F = 0uy } }

    let jumped = Cpu.step setCarry bus
    let notJumped = Cpu.step clearCarry bus

    Assert.Equal(0x2000us, jumped.Cpu.Registers.PC)
    Assert.Equal(16, jumped.Cycles)
    Assert.Equal(0x0103us, notJumped.Cpu.Registers.PC)
    Assert.Equal(12, notJumped.Cycles)

[<Fact>]
let ``Emulator run stops at unsupported opcode with current session`` () =
    let bus = makeBus [| 0x00uy; 0xD3uy |]
    let session: Emulator.Session =
        { Cpu = Cpu.initialState
          Bus = bus
          TotalCycles = 0L
          Steps = 0 }

    let result = Emulator.run 10 session

    Assert.Equal(1, result.Session.Steps)
    Assert.Equal(4L, result.Session.TotalCycles)
    Assert.Equal(0x0101us, result.Session.Cpu.Registers.PC)

    match result.StopReason with
    | Emulator.UnsupportedOpcode(opcode, pc) ->
        Assert.Equal(0xD3uy, opcode)
        Assert.Equal(0x0101us, pc)
    | other -> Assert.Fail $"Unexpected stop reason: {other}"

[<Fact>]
let ``Emulator run handles large step counts without growing the stack`` () =
    let bus = makeBus [| 0x18uy; 0xFEuy |]
    let session: Emulator.Session =
        { Cpu = Cpu.initialState
          Bus = bus
          TotalCycles = 0L
          Steps = 0 }

    let result = Emulator.run 100_000 session

    Assert.Equal(100_000, result.Session.Steps)
    Assert.Equal(1_200_000L, result.Session.TotalCycles)
    Assert.Equal(0x0100us, result.Session.Cpu.Registers.PC)
    Assert.Equal(Emulator.StepLimitReached, result.StopReason)

[<Fact>]
let ``Emulator run continues ticking while CPU is halted`` () =
    let bus = makeBus [| 0x76uy |]
    let session: Emulator.Session =
        { Cpu = Cpu.initialState
          Bus = bus
          TotalCycles = 0L
          Steps = 0 }

    let result = Emulator.run 3 session

    Assert.True(result.Session.Cpu.Halted)
    Assert.Equal(3, result.Session.Steps)
    Assert.Equal(12L, result.Session.TotalCycles)
    Assert.Equal(Emulator.StepLimitReached, result.StopReason)

[<Fact>]
let ``CALL a16 pushes return address and jumps`` () =
    let bus = makeBus [| 0xCDuy; 0x00uy; 0x20uy |]
    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with SP = 0xD000us } }

    let result = Cpu.step cpu bus

    Assert.Equal(0x2000us, result.Cpu.Registers.PC)
    Assert.Equal(0xCFFEus, result.Cpu.Registers.SP)
    Assert.Equal(0x03uy, Bus.readByte 0xCFFEus result.Bus)
    Assert.Equal(0x01uy, Bus.readByte 0xCFFFus result.Bus)
    Assert.Equal(24, result.Cycles)

[<Fact>]
let ``CALL Z calls only when zero flag is set`` () =
    let bus = makeBus [| 0xCCuy; 0x00uy; 0x20uy |]
    let setZero = { Cpu.initialState with Registers = { Cpu.initialRegisters with F = Cpu.ZeroFlag; SP = 0xD000us } }
    let clearZero = { Cpu.initialState with Registers = { Cpu.initialRegisters with F = 0uy; SP = 0xD000us } }

    let called = Cpu.step setZero bus
    let notCalled = Cpu.step clearZero bus

    Assert.Equal(0x2000us, called.Cpu.Registers.PC)
    Assert.Equal(0xCFFEus, called.Cpu.Registers.SP)
    Assert.Equal(0x03uy, Bus.readByte 0xCFFEus called.Bus)
    Assert.Equal(0x01uy, Bus.readByte 0xCFFFus called.Bus)
    Assert.Equal(24, called.Cycles)
    Assert.Equal(0x0103us, notCalled.Cpu.Registers.PC)
    Assert.Equal(0xD000us, notCalled.Cpu.Registers.SP)
    Assert.Equal(12, notCalled.Cycles)

[<Fact>]
let ``CALL NZ calls only when zero flag is clear`` () =
    let bus = makeBus [| 0xC4uy; 0x00uy; 0x20uy |]
    let clearZero = { Cpu.initialState with Registers = { Cpu.initialRegisters with F = 0uy; SP = 0xD000us } }
    let setZero = { Cpu.initialState with Registers = { Cpu.initialRegisters with F = Cpu.ZeroFlag; SP = 0xD000us } }

    let called = Cpu.step clearZero bus
    let notCalled = Cpu.step setZero bus

    Assert.Equal(0x2000us, called.Cpu.Registers.PC)
    Assert.Equal(0xCFFEus, called.Cpu.Registers.SP)
    Assert.Equal(0x03uy, Bus.readByte 0xCFFEus called.Bus)
    Assert.Equal(0x01uy, Bus.readByte 0xCFFFus called.Bus)
    Assert.Equal(24, called.Cycles)
    Assert.Equal(0x0103us, notCalled.Cpu.Registers.PC)
    Assert.Equal(0xD000us, notCalled.Cpu.Registers.SP)
    Assert.Equal(12, notCalled.Cycles)

[<Fact>]
let ``RST 28 pushes return address and jumps to vector`` () =
    let bus = makeBus [| 0xEFuy |]
    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with SP = 0xD000us } }

    let result = Cpu.step cpu bus

    Assert.Equal(0x0028us, result.Cpu.Registers.PC)
    Assert.Equal(0xCFFEus, result.Cpu.Registers.SP)
    Assert.Equal(0x01uy, Bus.readByte 0xCFFEus result.Bus)
    Assert.Equal(0x01uy, Bus.readByte 0xCFFFus result.Bus)
    Assert.Equal(16, result.Cycles)

[<Fact>]
let ``RST 20 pushes return address and jumps to vector`` () =
    let bus = makeBus [| 0xE7uy |]
    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with SP = 0xD000us } }

    let result = Cpu.step cpu bus

    Assert.Equal(0x0020us, result.Cpu.Registers.PC)
    Assert.Equal(0xCFFEus, result.Cpu.Registers.SP)
    Assert.Equal(0x01uy, Bus.readByte 0xCFFEus result.Bus)
    Assert.Equal(0x01uy, Bus.readByte 0xCFFFus result.Bus)
    Assert.Equal(16, result.Cycles)

[<Fact>]
let ``RST 38 pushes return address and jumps to vector`` () =
    let bus = makeBus [| 0xFFuy |]
    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with SP = 0xD000us } }

    let result = Cpu.step cpu bus

    Assert.Equal(0x0038us, result.Cpu.Registers.PC)
    Assert.Equal(0xCFFEus, result.Cpu.Registers.SP)
    Assert.Equal(0x01uy, Bus.readByte 0xCFFEus result.Bus)
    Assert.Equal(0x01uy, Bus.readByte 0xCFFFus result.Bus)
    Assert.Equal(16, result.Cycles)

[<Fact>]
let ``RET pops PC from stack`` () =
    let bus =
        makeBus [| 0xC9uy |]
        |> Bus.writeByte 0xC000us 0x34uy
        |> Bus.writeByte 0xC001us 0x12uy

    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with SP = 0xC000us } }
    let result = Cpu.step cpu bus

    Assert.Equal(0x1234us, result.Cpu.Registers.PC)
    Assert.Equal(0xC002us, result.Cpu.Registers.SP)
    Assert.Equal(16, result.Cycles)

[<Fact>]
let ``RETI pops PC and enables interrupts`` () =
    let bus =
        makeBus [| 0xD9uy |]
        |> Bus.writeByte 0xC000us 0x34uy
        |> Bus.writeByte 0xC001us 0x12uy

    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with SP = 0xC000us }; InterruptsEnabled = false }
    let result = Cpu.step cpu bus

    Assert.Equal(0x1234us, result.Cpu.Registers.PC)
    Assert.Equal(0xC002us, result.Cpu.Registers.SP)
    Assert.True(result.Cpu.InterruptsEnabled)
    Assert.Equal(16, result.Cycles)

[<Fact>]
let ``RET Z returns only when zero flag is set`` () =
    let bus =
        makeBus [| 0xC8uy |]
        |> Bus.writeByte 0xC000us 0x34uy
        |> Bus.writeByte 0xC001us 0x12uy

    let setZero = { Cpu.initialState with Registers = { Cpu.initialRegisters with F = Cpu.ZeroFlag; SP = 0xC000us } }
    let clearZero = { Cpu.initialState with Registers = { Cpu.initialRegisters with F = 0uy; SP = 0xC000us } }

    let returned = Cpu.step setZero bus
    let notReturned = Cpu.step clearZero bus

    Assert.Equal(0x1234us, returned.Cpu.Registers.PC)
    Assert.Equal(0xC002us, returned.Cpu.Registers.SP)
    Assert.Equal(20, returned.Cycles)
    Assert.Equal(0x0101us, notReturned.Cpu.Registers.PC)
    Assert.Equal(0xC000us, notReturned.Cpu.Registers.SP)
    Assert.Equal(8, notReturned.Cycles)

[<Fact>]
let ``RET NC returns only when carry flag is clear`` () =
    let bus =
        makeBus [| 0xD0uy |]
        |> Bus.writeByte 0xC000us 0x34uy
        |> Bus.writeByte 0xC001us 0x12uy

    let clearCarry = { Cpu.initialState with Registers = { Cpu.initialRegisters with F = 0uy; SP = 0xC000us } }
    let setCarry = { Cpu.initialState with Registers = { Cpu.initialRegisters with F = Cpu.CarryFlag; SP = 0xC000us } }

    let returned = Cpu.step clearCarry bus
    let notReturned = Cpu.step setCarry bus

    Assert.Equal(0x1234us, returned.Cpu.Registers.PC)
    Assert.Equal(0xC002us, returned.Cpu.Registers.SP)
    Assert.Equal(20, returned.Cycles)
    Assert.Equal(0x0101us, notReturned.Cpu.Registers.PC)
    Assert.Equal(0xC000us, notReturned.Cpu.Registers.SP)
    Assert.Equal(8, notReturned.Cycles)

[<Fact>]
let ``RET C returns only when carry flag is set`` () =
    let bus =
        makeBus [| 0xD8uy |]
        |> Bus.writeByte 0xC000us 0x34uy
        |> Bus.writeByte 0xC001us 0x12uy

    let setCarry = { Cpu.initialState with Registers = { Cpu.initialRegisters with F = Cpu.CarryFlag; SP = 0xC000us } }
    let clearCarry = { Cpu.initialState with Registers = { Cpu.initialRegisters with F = 0uy; SP = 0xC000us } }

    let returned = Cpu.step setCarry bus
    let notReturned = Cpu.step clearCarry bus

    Assert.Equal(0x1234us, returned.Cpu.Registers.PC)
    Assert.Equal(0xC002us, returned.Cpu.Registers.SP)
    Assert.Equal(20, returned.Cycles)
    Assert.Equal(0x0101us, notReturned.Cpu.Registers.PC)
    Assert.Equal(0xC000us, notReturned.Cpu.Registers.SP)
    Assert.Equal(8, notReturned.Cycles)

[<Fact>]
let ``RET NZ returns only when zero flag is clear`` () =
    let bus =
        makeBus [| 0xC0uy |]
        |> Bus.writeByte 0xC000us 0x34uy
        |> Bus.writeByte 0xC001us 0x12uy

    let clearZero = { Cpu.initialState with Registers = { Cpu.initialRegisters with F = 0uy; SP = 0xC000us } }
    let setZero = { Cpu.initialState with Registers = { Cpu.initialRegisters with F = Cpu.ZeroFlag; SP = 0xC000us } }

    let returned = Cpu.step clearZero bus
    let notReturned = Cpu.step setZero bus

    Assert.Equal(0x1234us, returned.Cpu.Registers.PC)
    Assert.Equal(0xC002us, returned.Cpu.Registers.SP)
    Assert.Equal(20, returned.Cycles)
    Assert.Equal(0x0101us, notReturned.Cpu.Registers.PC)
    Assert.Equal(0xC000us, notReturned.Cpu.Registers.SP)
    Assert.Equal(8, notReturned.Cycles)

[<Fact>]
let ``PUSH HL stores HL on stack`` () =
    let bus = makeBus [| 0xE5uy |]
    let cpu =
        { Cpu.initialState with
            Registers =
                { Cpu.initialRegisters with
                    H = 0x12uy
                    L = 0x34uy
                    SP = 0xD000us } }

    let result = Cpu.step cpu bus

    Assert.Equal(0xCFFEus, result.Cpu.Registers.SP)
    Assert.Equal(0x34uy, Bus.readByte 0xCFFEus result.Bus)
    Assert.Equal(0x12uy, Bus.readByte 0xCFFFus result.Bus)
    Assert.Equal(0x0101us, result.Cpu.Registers.PC)
    Assert.Equal(16, result.Cycles)

[<Fact>]
let ``PUSH BC stores BC on stack`` () =
    let bus = makeBus [| 0xC5uy |]
    let cpu =
        { Cpu.initialState with
            Registers =
                { Cpu.initialRegisters with
                    B = 0xBEuy
                    C = 0xEFuy
                    SP = 0xD000us } }

    let result = Cpu.step cpu bus

    Assert.Equal(0xCFFEus, result.Cpu.Registers.SP)
    Assert.Equal(0xEFuy, Bus.readByte 0xCFFEus result.Bus)
    Assert.Equal(0xBEuy, Bus.readByte 0xCFFFus result.Bus)
    Assert.Equal(0x0101us, result.Cpu.Registers.PC)
    Assert.Equal(16, result.Cycles)

[<Fact>]
let ``PUSH DE stores DE on stack`` () =
    let bus = makeBus [| 0xD5uy |]
    let cpu =
        { Cpu.initialState with
            Registers =
                { Cpu.initialRegisters with
                    D = 0xDEuy
                    E = 0xADuy
                    SP = 0xD000us } }

    let result = Cpu.step cpu bus

    Assert.Equal(0xCFFEus, result.Cpu.Registers.SP)
    Assert.Equal(0xADuy, Bus.readByte 0xCFFEus result.Bus)
    Assert.Equal(0xDEuy, Bus.readByte 0xCFFFus result.Bus)
    Assert.Equal(0x0101us, result.Cpu.Registers.PC)
    Assert.Equal(16, result.Cycles)

[<Theory>]
[<InlineData(0xC1uy, "BC")>]
[<InlineData(0xD1uy, "DE")>]
[<InlineData(0xE1uy, "HL")>]
let ``POP register pair restores selected register pair`` opcode registerPair =
    let bus =
        makeBus [| opcode |]
        |> Bus.writeByte 0xC000us 0x34uy
        |> Bus.writeByte 0xC001us 0x12uy

    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with SP = 0xC000us } }
    let result = Cpu.step cpu bus

    match registerPair with
    | "BC" ->
        Assert.Equal(0x12uy, result.Cpu.Registers.B)
        Assert.Equal(0x34uy, result.Cpu.Registers.C)
    | "DE" ->
        Assert.Equal(0x12uy, result.Cpu.Registers.D)
        Assert.Equal(0x34uy, result.Cpu.Registers.E)
    | "HL" ->
        Assert.Equal(0x12uy, result.Cpu.Registers.H)
        Assert.Equal(0x34uy, result.Cpu.Registers.L)
    | other -> Assert.Fail $"Unexpected pair: {other}"

    Assert.Equal(0xC002us, result.Cpu.Registers.SP)
    Assert.Equal(0x0101us, result.Cpu.Registers.PC)
    Assert.Equal(12, result.Cycles)

[<Fact>]
let ``PUSH AF stores AF on stack with masked flag low nibble`` () =
    let bus = makeBus [| 0xF5uy |]
    let cpu =
        { Cpu.initialState with
            Registers =
                { Cpu.initialRegisters with
                    A = 0x89uy
                    F = 0xBFuy
                    SP = 0xD000us } }

    let result = Cpu.step cpu bus

    Assert.Equal(0xCFFEus, result.Cpu.Registers.SP)
    Assert.Equal(0xB0uy, Bus.readByte 0xCFFEus result.Bus)
    Assert.Equal(0x89uy, Bus.readByte 0xCFFFus result.Bus)
    Assert.Equal(0x0101us, result.Cpu.Registers.PC)
    Assert.Equal(16, result.Cycles)

[<Fact>]
let ``POP AF restores AF and masks flag low nibble`` () =
    let bus =
        makeBus [| 0xF1uy |]
        |> Bus.writeByte 0xC000us 0xBFuy
        |> Bus.writeByte 0xC001us 0x89uy

    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with A = 0uy; F = 0uy; SP = 0xC000us } }
    let result = Cpu.step cpu bus

    Assert.Equal(0x89uy, result.Cpu.Registers.A)
    Assert.Equal(0xB0uy, result.Cpu.Registers.F)
    Assert.Equal(0xC002us, result.Cpu.Registers.SP)
    Assert.Equal(0x0101us, result.Cpu.Registers.PC)
    Assert.Equal(12, result.Cycles)

[<Fact>]
let ``LDH A a8 reads from high IO page`` () =
    let bus =
        makeBus [| 0xF0uy; 0x80uy |]
        |> Bus.writeByte 0xFF80us 0x5Auy

    let result = Cpu.step Cpu.initialState bus

    Assert.Equal(0x5Auy, result.Cpu.Registers.A)
    Assert.Equal(0x0102us, result.Cpu.Registers.PC)
    Assert.Equal(12, result.Cycles)

[<Fact>]
let ``LD A C reads from high IO page addressed by C`` () =
    let bus =
        makeBus [| 0xF2uy |]
        |> Bus.writeByte 0xFF80us 0xA6uy

    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with C = 0x80uy } }
    let result = Cpu.step cpu bus

    Assert.Equal(0xA6uy, result.Cpu.Registers.A)
    Assert.Equal(0x0101us, result.Cpu.Registers.PC)
    Assert.Equal(8, result.Cycles)

[<Fact>]
let ``LD A a16 reads through bus`` () =
    let bus =
        makeBus [| 0xFAuy; 0x34uy; 0xC1uy |]
        |> Bus.writeByte 0xC134us 0xADuy

    let result = Cpu.step Cpu.initialState bus

    Assert.Equal(0xADuy, result.Cpu.Registers.A)
    Assert.Equal(0x0103us, result.Cpu.Registers.PC)
    Assert.Equal(16, result.Cycles)

[<Fact>]
let ``LD HL SP plus signed offset updates flags`` () =
    let bus = makeBus [| 0xF8uy; 0x01uy |]
    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with SP = 0x00FFus; F = Cpu.ZeroFlag } }

    let result = Cpu.step cpu bus

    Assert.Equal(0x01uy, result.Cpu.Registers.H)
    Assert.Equal(0x00uy, result.Cpu.Registers.L)
    Assert.Equal(Cpu.HalfCarryFlag ||| Cpu.CarryFlag, result.Cpu.Registers.F)
    Assert.Equal(0x0102us, result.Cpu.Registers.PC)
    Assert.Equal(12, result.Cycles)

[<Fact>]
let ``LD HL SP plus negative offset stores wrapped result`` () =
    let bus = makeBus [| 0xF8uy; 0xFEuy |]
    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with SP = 0x0001us; F = Cpu.ZeroFlag } }

    let result = Cpu.step cpu bus

    Assert.Equal(0xFFuy, result.Cpu.Registers.H)
    Assert.Equal(0xFFuy, result.Cpu.Registers.L)
    Assert.Equal(0uy, result.Cpu.Registers.F)
    Assert.Equal(0x0102us, result.Cpu.Registers.PC)
    Assert.Equal(12, result.Cycles)

[<Fact>]
let ``LD SP HL copies HL into stack pointer`` () =
    let bus = makeBus [| 0xF9uy |]
    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with H = 0xC1uy; L = 0x23uy; SP = 0us } }

    let result = Cpu.step cpu bus

    Assert.Equal(0xC123us, result.Cpu.Registers.SP)
    Assert.Equal(0x0101us, result.Cpu.Registers.PC)
    Assert.Equal(8, result.Cycles)

[<Fact>]
let ``EI advances PC and consumes four cycles`` () =
    let bus = makeBus [| 0xFBuy |]
    let result = Cpu.step Cpu.initialState bus

    Assert.True(result.Cpu.InterruptsEnabled)
    Assert.Equal(0x0101us, result.Cpu.Registers.PC)
    Assert.Equal(4, result.Cycles)

[<Fact>]
let ``DI disables interrupt servicing`` () =
    let bus = makeBus [| 0xF3uy |]
    let cpu = { Cpu.initialState with InterruptsEnabled = true }

    let result = Cpu.step cpu bus

    Assert.False(result.Cpu.InterruptsEnabled)
    Assert.Equal(0x0101us, result.Cpu.Registers.PC)
    Assert.Equal(4, result.Cycles)

[<Fact>]
let ``enabled interrupt pushes PC clears IF and jumps to vector`` () =
    let bus =
        makeBus [| 0x00uy |]
        |> Bus.writeByte 0xFFFFus Interrupt.VBlankBit
        |> Bus.writeByte 0xFF0Fus Interrupt.VBlankBit

    let cpu =
        { Cpu.initialState with
            InterruptsEnabled = true
            Registers = { Cpu.initialRegisters with SP = 0xD000us; PC = 0x1234us } }

    let result = Cpu.step cpu bus

    Assert.False(result.Cpu.InterruptsEnabled)
    Assert.False(result.Cpu.Halted)
    Assert.Equal(0x0040us, result.Cpu.Registers.PC)
    Assert.Equal(0xCFFEus, result.Cpu.Registers.SP)
    Assert.Equal(0x34uy, Bus.readByte 0xCFFEus result.Bus)
    Assert.Equal(0x12uy, Bus.readByte 0xCFFFus result.Bus)
    Assert.Equal(0uy, Bus.readByte 0xFF0Fus result.Bus &&& Interrupt.VBlankBit)
    Assert.Equal(20, result.Cycles)

[<Fact>]
let ``halted CPU resumes through enabled pending interrupt`` () =
    let bus =
        makeBus [| 0x00uy |]
        |> Bus.writeByte 0xFFFFus Interrupt.VBlankBit
        |> Bus.writeByte 0xFF0Fus Interrupt.VBlankBit

    let cpu =
        { Cpu.initialState with
            Halted = true
            InterruptsEnabled = true
            Registers = { Cpu.initialRegisters with SP = 0xD000us; PC = 0x4567us } }

    let result = Cpu.step cpu bus

    Assert.False(result.Cpu.Halted)
    Assert.Equal(0x0040us, result.Cpu.Registers.PC)
    Assert.Equal(0xCFFEus, result.Cpu.Registers.SP)
    Assert.Equal(20, result.Cycles)

[<Fact>]
let ``LD B A copies accumulator`` () =
    let bus = makeBus [| 0x47uy |]
    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with A = 0x99uy; B = 0x00uy } }

    let result = Cpu.step cpu bus

    Assert.Equal(0x99uy, result.Cpu.Registers.B)
    Assert.Equal(0x0101us, result.Cpu.Registers.PC)
    Assert.Equal(4, result.Cycles)

[<Theory>]
[<InlineData(0x40uy, 0x11uy)>]
[<InlineData(0x41uy, 0x22uy)>]
[<InlineData(0x42uy, 0x33uy)>]
[<InlineData(0x43uy, 0x44uy)>]
[<InlineData(0x44uy, 0x55uy)>]
[<InlineData(0x45uy, 0x66uy)>]
[<InlineData(0x47uy, 0x77uy)>]
let ``LD B register copies selected register into B`` opcode expected =
    let bus = makeBus [| opcode |]
    let cpu =
        { Cpu.initialState with
            Registers =
                { Cpu.initialRegisters with
                    B = 0x11uy
                    C = 0x22uy
                    D = 0x33uy
                    E = 0x44uy
                    H = 0x55uy
                    L = 0x66uy
                    A = 0x77uy } }

    let result = Cpu.step cpu bus

    Assert.Equal(expected, result.Cpu.Registers.B)
    Assert.Equal(0x0101us, result.Cpu.Registers.PC)
    Assert.Equal(4, result.Cycles)

[<Fact>]
let ``LD B HL reads through bus`` () =
    let bus =
        makeBus [| 0x46uy |]
        |> Bus.writeByte 0xC044us 0x46uy

    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with H = 0xC0uy; L = 0x44uy } }
    let result = Cpu.step cpu bus

    Assert.Equal(0x46uy, result.Cpu.Registers.B)
    Assert.Equal(0x0101us, result.Cpu.Registers.PC)
    Assert.Equal(8, result.Cycles)

[<Fact>]
let ``LD C A copies accumulator`` () =
    let bus = makeBus [| 0x4Fuy |]
    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with A = 0x4Cuy; C = 0x00uy } }

    let result = Cpu.step cpu bus

    Assert.Equal(0x4Cuy, result.Cpu.Registers.C)
    Assert.Equal(0x0101us, result.Cpu.Registers.PC)
    Assert.Equal(4, result.Cycles)

[<Fact>]
let ``LD C B copies B into C`` () =
    let bus = makeBus [| 0x48uy |]
    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with B = 0x48uy; C = 0uy } }

    let result = Cpu.step cpu bus

    Assert.Equal(0x48uy, result.Cpu.Registers.C)
    Assert.Equal(0x0101us, result.Cpu.Registers.PC)
    Assert.Equal(4, result.Cycles)

[<Fact>]
let ``LD C D copies D into C`` () =
    let bus = makeBus [| 0x4Auy |]
    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with C = 0uy; D = 0x4Auy } }

    let result = Cpu.step cpu bus

    Assert.Equal(0x4Auy, result.Cpu.Registers.C)
    Assert.Equal(0x0101us, result.Cpu.Registers.PC)
    Assert.Equal(4, result.Cycles)

[<Fact>]
let ``LD C HL reads through bus`` () =
    let bus =
        makeBus [| 0x4Euy |]
        |> Bus.writeByte 0xC040us 0x4Euy

    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with H = 0xC0uy; L = 0x40uy } }
    let result = Cpu.step cpu bus

    Assert.Equal(0x4Euy, result.Cpu.Registers.C)
    Assert.Equal(0x0101us, result.Cpu.Registers.PC)
    Assert.Equal(8, result.Cycles)

[<Fact>]
let ``LD C L copies L into C`` () =
    let bus = makeBus [| 0x4Duy |]
    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with C = 0uy; L = 0x4Duy } }

    let result = Cpu.step cpu bus

    Assert.Equal(0x4Duy, result.Cpu.Registers.C)
    Assert.Equal(0x0101us, result.Cpu.Registers.PC)
    Assert.Equal(4, result.Cycles)

[<Fact>]
let ``LD D A copies accumulator`` () =
    let bus = makeBus [| 0x57uy |]
    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with A = 0x55uy; D = 0x00uy } }

    let result = Cpu.step cpu bus

    Assert.Equal(0x55uy, result.Cpu.Registers.D)
    Assert.Equal(0x0101us, result.Cpu.Registers.PC)
    Assert.Equal(4, result.Cycles)

[<Fact>]
let ``LD D HL reads through bus`` () =
    let bus =
        makeBus [| 0x56uy |]
        |> Bus.writeByte 0xC042us 0x56uy

    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with H = 0xC0uy; L = 0x42uy } }
    let result = Cpu.step cpu bus

    Assert.Equal(0x56uy, result.Cpu.Registers.D)
    Assert.Equal(0x0101us, result.Cpu.Registers.PC)
    Assert.Equal(8, result.Cycles)

[<Theory>]
[<InlineData(0x50uy, 0x11uy)>]
[<InlineData(0x51uy, 0x22uy)>]
[<InlineData(0x52uy, 0x33uy)>]
[<InlineData(0x53uy, 0x44uy)>]
[<InlineData(0x54uy, 0x55uy)>]
[<InlineData(0x55uy, 0x66uy)>]
[<InlineData(0x57uy, 0x77uy)>]
let ``LD D register copies selected register into D`` opcode expected =
    let bus = makeBus [| opcode |]
    let cpu =
        { Cpu.initialState with
            Registers =
                { Cpu.initialRegisters with
                    B = 0x11uy
                    C = 0x22uy
                    D = 0x33uy
                    E = 0x44uy
                    H = 0x55uy
                    L = 0x66uy
                    A = 0x77uy } }

    let result = Cpu.step cpu bus

    Assert.Equal(expected, result.Cpu.Registers.D)
    Assert.Equal(0x0101us, result.Cpu.Registers.PC)
    Assert.Equal(4, result.Cycles)

[<Theory>]
[<InlineData(0x58uy, 0x11uy)>]
[<InlineData(0x59uy, 0x22uy)>]
[<InlineData(0x5Auy, 0x33uy)>]
[<InlineData(0x5Buy, 0x44uy)>]
[<InlineData(0x5Cuy, 0x55uy)>]
[<InlineData(0x5Duy, 0x66uy)>]
[<InlineData(0x5Fuy, 0x77uy)>]
let ``LD E register copies selected register into E`` opcode expected =
    let bus = makeBus [| opcode |]
    let cpu =
        { Cpu.initialState with
            Registers =
                { Cpu.initialRegisters with
                    B = 0x11uy
                    C = 0x22uy
                    D = 0x33uy
                    E = 0x44uy
                    H = 0x55uy
                    L = 0x66uy
                    A = 0x77uy } }

    let result = Cpu.step cpu bus

    Assert.Equal(expected, result.Cpu.Registers.E)
    Assert.Equal(0x0101us, result.Cpu.Registers.PC)
    Assert.Equal(4, result.Cycles)

[<Fact>]
let ``LD E HL reads through bus`` () =
    let bus =
        makeBus [| 0x5Euy |]
        |> Bus.writeByte 0xC041us 0x5Euy

    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with H = 0xC0uy; L = 0x41uy } }
    let result = Cpu.step cpu bus

    Assert.Equal(0x5Euy, result.Cpu.Registers.E)
    Assert.Equal(0x0101us, result.Cpu.Registers.PC)
    Assert.Equal(8, result.Cycles)

[<Fact>]
let ``LD L A copies accumulator`` () =
    let bus = makeBus [| 0x6Fuy |]
    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with A = 0x6Fuy; L = 0x00uy } }

    let result = Cpu.step cpu bus

    Assert.Equal(0x6Fuy, result.Cpu.Registers.L)
    Assert.Equal(0x0101us, result.Cpu.Registers.PC)
    Assert.Equal(4, result.Cycles)

[<Fact>]
let ``LD L HL reads through bus`` () =
    let bus =
        makeBus [| 0x6Euy |]
        |> Bus.writeByte 0xC020us 0x6Euy

    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with H = 0xC0uy; L = 0x20uy } }
    let result = Cpu.step cpu bus

    Assert.Equal(0x6Euy, result.Cpu.Registers.L)
    Assert.Equal(0x0101us, result.Cpu.Registers.PC)
    Assert.Equal(8, result.Cycles)

[<Fact>]
let ``LD A B copies B into accumulator`` () =
    let bus = makeBus [| 0x78uy |]
    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with A = 0x00uy; B = 0xABuy } }

    let result = Cpu.step cpu bus

    Assert.Equal(0xABuy, result.Cpu.Registers.A)
    Assert.Equal(0x0101us, result.Cpu.Registers.PC)
    Assert.Equal(4, result.Cycles)

[<Fact>]
let ``LD A C copies C into accumulator`` () =
    let bus = makeBus [| 0x79uy |]
    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with A = 0x00uy; C = 0xC9uy } }

    let result = Cpu.step cpu bus

    Assert.Equal(0xC9uy, result.Cpu.Registers.A)
    Assert.Equal(0x0101us, result.Cpu.Registers.PC)
    Assert.Equal(4, result.Cycles)

[<Fact>]
let ``LD A D copies D into accumulator`` () =
    let bus = makeBus [| 0x7Auy |]
    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with A = 0x00uy; D = 0xD5uy } }

    let result = Cpu.step cpu bus

    Assert.Equal(0xD5uy, result.Cpu.Registers.A)
    Assert.Equal(0x0101us, result.Cpu.Registers.PC)
    Assert.Equal(4, result.Cycles)

[<Fact>]
let ``LD A E copies E into accumulator`` () =
    let bus = makeBus [| 0x7Buy |]
    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with A = 0x00uy; E = 0xE5uy } }

    let result = Cpu.step cpu bus

    Assert.Equal(0xE5uy, result.Cpu.Registers.A)
    Assert.Equal(0x0101us, result.Cpu.Registers.PC)
    Assert.Equal(4, result.Cycles)

[<Fact>]
let ``LD A L copies L into accumulator`` () =
    let bus = makeBus [| 0x7Duy |]
    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with A = 0x00uy; L = 0xEEuy } }

    let result = Cpu.step cpu bus

    Assert.Equal(0xEEuy, result.Cpu.Registers.A)
    Assert.Equal(0x0101us, result.Cpu.Registers.PC)
    Assert.Equal(4, result.Cycles)

[<Fact>]
let ``LD A A preserves accumulator`` () =
    let bus = makeBus [| 0x7Fuy |]
    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with A = 0x7Fuy } }

    let result = Cpu.step cpu bus

    Assert.Equal(0x7Fuy, result.Cpu.Registers.A)
    Assert.Equal(0x0101us, result.Cpu.Registers.PC)
    Assert.Equal(4, result.Cycles)

[<Fact>]
let ``LD A HL reads through bus`` () =
    let bus =
        makeBus [| 0x7Euy |]
        |> Bus.writeByte 0xC043us 0x7Euy

    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with H = 0xC0uy; L = 0x43uy } }
    let result = Cpu.step cpu bus

    Assert.Equal(0x7Euy, result.Cpu.Registers.A)
    Assert.Equal(0x0101us, result.Cpu.Registers.PC)
    Assert.Equal(8, result.Cycles)

[<Theory>]
[<InlineData(0x60uy, 0x11uy)>]
[<InlineData(0x61uy, 0x22uy)>]
[<InlineData(0x62uy, 0x33uy)>]
[<InlineData(0x63uy, 0x44uy)>]
[<InlineData(0x64uy, 0x55uy)>]
[<InlineData(0x65uy, 0x66uy)>]
[<InlineData(0x67uy, 0x77uy)>]
let ``LD H register copies selected register into H`` opcode expected =
    let bus = makeBus [| opcode |]
    let cpu =
        { Cpu.initialState with
            Registers =
                { Cpu.initialRegisters with
                    B = 0x11uy
                    C = 0x22uy
                    D = 0x33uy
                    E = 0x44uy
                    H = 0x55uy
                    L = 0x66uy
                    A = 0x77uy } }

    let result = Cpu.step cpu bus

    Assert.Equal(expected, result.Cpu.Registers.H)
    Assert.Equal(0x0101us, result.Cpu.Registers.PC)
    Assert.Equal(4, result.Cycles)

[<Fact>]
let ``LD H HL reads through bus into H using original HL address`` () =
    let bus =
        makeBus [| 0x66uy |]
        |> Bus.writeByte 0xC020us 0x99uy

    let cpu =
        { Cpu.initialState with
            Registers =
                { Cpu.initialRegisters with
                    H = 0xC0uy
                    L = 0x20uy } }

    let result = Cpu.step cpu bus

    Assert.Equal(0x99uy, result.Cpu.Registers.H)
    Assert.Equal(0x20uy, result.Cpu.Registers.L)
    Assert.Equal(0x0101us, result.Cpu.Registers.PC)
    Assert.Equal(8, result.Cycles)

[<Theory>]
[<InlineData(0x68uy, 0x11uy)>]
[<InlineData(0x69uy, 0x22uy)>]
[<InlineData(0x6Auy, 0x33uy)>]
[<InlineData(0x6Buy, 0x44uy)>]
[<InlineData(0x6Cuy, 0x55uy)>]
[<InlineData(0x6Duy, 0x66uy)>]
let ``LD L register copies selected register into L`` opcode expected =
    let bus = makeBus [| opcode |]
    let cpu =
        { Cpu.initialState with
            Registers =
                { Cpu.initialRegisters with
                    B = 0x11uy
                    C = 0x22uy
                    D = 0x33uy
                    E = 0x44uy
                    H = 0x55uy
                    L = 0x66uy } }

    let result = Cpu.step cpu bus

    Assert.Equal(expected, result.Cpu.Registers.L)
    Assert.Equal(0x0101us, result.Cpu.Registers.PC)
    Assert.Equal(4, result.Cycles)

[<Theory>]
[<InlineData(0x80uy)>]
[<InlineData(0x81uy)>]
[<InlineData(0x82uy)>]
[<InlineData(0x83uy)>]
[<InlineData(0x84uy)>]
[<InlineData(0x85uy)>]
[<InlineData(0x87uy)>]
let ``ADD A register adds selected register into accumulator`` opcode =
    let bus = makeBus [| opcode |]
    let cpu =
        { Cpu.initialState with
            Registers =
                { Cpu.initialRegisters with
                    A = 0x10uy
                    B = 0x22uy
                    C = 0x22uy
                    D = 0x22uy
                    E = 0x22uy
                    H = 0x22uy
                    L = 0x22uy } }

    let result = Cpu.step cpu bus

    let expected = if opcode = 0x87uy then 0x20uy else 0x32uy
    Assert.Equal(expected, result.Cpu.Registers.A)
    Assert.Equal(0uy, result.Cpu.Registers.F)
    Assert.Equal(0x0101us, result.Cpu.Registers.PC)
    Assert.Equal(4, result.Cycles)

[<Fact>]
let ``ADD A register sets half carry carry and zero`` () =
    let bus = makeBus [| 0x81uy |]
    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with A = 0xFFuy; C = 0x01uy } }

    let result = Cpu.step cpu bus

    Assert.Equal(0uy, result.Cpu.Registers.A)
    Assert.Equal(Cpu.ZeroFlag ||| Cpu.HalfCarryFlag ||| Cpu.CarryFlag, result.Cpu.Registers.F)
    Assert.Equal(0x0101us, result.Cpu.Registers.PC)
    Assert.Equal(4, result.Cycles)

[<Fact>]
let ``ADD A d8 adds immediate into accumulator`` () =
    let bus = makeBus [| 0xC6uy; 0x01uy |]
    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with A = 0xFFuy } }

    let result = Cpu.step cpu bus

    Assert.Equal(0x00uy, result.Cpu.Registers.A)
    Assert.Equal(Cpu.ZeroFlag ||| Cpu.HalfCarryFlag ||| Cpu.CarryFlag, result.Cpu.Registers.F)
    Assert.Equal(0x0102us, result.Cpu.Registers.PC)
    Assert.Equal(8, result.Cycles)

[<Fact>]
let ``ADD A HL adds memory into accumulator`` () =
    let bus =
        makeBus [| 0x86uy |]
        |> Bus.writeByte 0xC020us 0x01uy

    let cpu =
        { Cpu.initialState with
            Registers =
                { Cpu.initialRegisters with
                    A = 0xFFuy
                    H = 0xC0uy
                    L = 0x20uy } }

    let result = Cpu.step cpu bus

    Assert.Equal(0x00uy, result.Cpu.Registers.A)
    Assert.Equal(Cpu.ZeroFlag ||| Cpu.HalfCarryFlag ||| Cpu.CarryFlag, result.Cpu.Registers.F)
    Assert.Equal(0x0101us, result.Cpu.Registers.PC)
    Assert.Equal(8, result.Cycles)

[<Fact>]
let ``ADC A B adds register and carry into accumulator`` () =
    let bus = makeBus [| 0x88uy |]
    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with A = 0xFFuy; B = 0x00uy; F = Cpu.CarryFlag } }

    let result = Cpu.step cpu bus

    Assert.Equal(0x00uy, result.Cpu.Registers.A)
    Assert.Equal(Cpu.ZeroFlag ||| Cpu.HalfCarryFlag ||| Cpu.CarryFlag, result.Cpu.Registers.F)
    Assert.Equal(0x0101us, result.Cpu.Registers.PC)
    Assert.Equal(4, result.Cycles)

[<Fact>]
let ``ADC A C adds register and carry into accumulator`` () =
    let bus = makeBus [| 0x89uy |]
    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with A = 0x0Fuy; C = 0x00uy; F = Cpu.CarryFlag } }

    let result = Cpu.step cpu bus

    Assert.Equal(0x10uy, result.Cpu.Registers.A)
    Assert.Equal(Cpu.HalfCarryFlag, result.Cpu.Registers.F)
    Assert.Equal(0x0101us, result.Cpu.Registers.PC)
    Assert.Equal(4, result.Cycles)

[<Fact>]
let ``ADC A D adds register and carry into accumulator`` () =
    let bus = makeBus [| 0x8Auy |]
    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with A = 0x0Fuy; D = 0x00uy; F = Cpu.CarryFlag } }

    let result = Cpu.step cpu bus

    Assert.Equal(0x10uy, result.Cpu.Registers.A)
    Assert.Equal(Cpu.HalfCarryFlag, result.Cpu.Registers.F)
    Assert.Equal(0x0101us, result.Cpu.Registers.PC)
    Assert.Equal(4, result.Cycles)

[<Fact>]
let ``ADC A L adds register and carry into accumulator`` () =
    let bus = makeBus [| 0x8Duy |]
    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with A = 0x0Fuy; L = 0x00uy; F = Cpu.CarryFlag } }

    let result = Cpu.step cpu bus

    Assert.Equal(0x10uy, result.Cpu.Registers.A)
    Assert.Equal(Cpu.HalfCarryFlag, result.Cpu.Registers.F)
    Assert.Equal(0x0101us, result.Cpu.Registers.PC)
    Assert.Equal(4, result.Cycles)

[<Fact>]
let ``ADC A A adds accumulator and carry into accumulator`` () =
    let bus = makeBus [| 0x8Fuy |]
    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with A = 0x7Fuy; F = Cpu.CarryFlag } }

    let result = Cpu.step cpu bus

    Assert.Equal(0xFFuy, result.Cpu.Registers.A)
    Assert.Equal(Cpu.HalfCarryFlag, result.Cpu.Registers.F)
    Assert.Equal(0x0101us, result.Cpu.Registers.PC)
    Assert.Equal(4, result.Cycles)

[<Fact>]
let ``ADC A HL adds memory and carry into accumulator`` () =
    let bus =
        makeBus [| 0x8Euy |]
        |> Bus.writeByte 0xC020us 0x00uy

    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with A = 0x0Fuy; H = 0xC0uy; L = 0x20uy; F = Cpu.CarryFlag } }
    let result = Cpu.step cpu bus

    Assert.Equal(0x10uy, result.Cpu.Registers.A)
    Assert.Equal(Cpu.HalfCarryFlag, result.Cpu.Registers.F)
    Assert.Equal(0x0101us, result.Cpu.Registers.PC)
    Assert.Equal(8, result.Cycles)

[<Fact>]
let ``ADC A d8 adds immediate and carry into accumulator`` () =
    let bus = makeBus [| 0xCEuy; 0x00uy |]
    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with A = 0x0Fuy; F = Cpu.CarryFlag } }

    let result = Cpu.step cpu bus

    Assert.Equal(0x10uy, result.Cpu.Registers.A)
    Assert.Equal(Cpu.HalfCarryFlag, result.Cpu.Registers.F)
    Assert.Equal(0x0102us, result.Cpu.Registers.PC)
    Assert.Equal(8, result.Cycles)

[<Theory>]
[<InlineData(0x90uy)>]
[<InlineData(0x91uy)>]
[<InlineData(0x92uy)>]
[<InlineData(0x93uy)>]
[<InlineData(0x94uy)>]
[<InlineData(0x95uy)>]
[<InlineData(0x97uy)>]
let ``SUB A register subtracts selected register from accumulator`` opcode =
    let bus = makeBus [| opcode |]
    let cpu =
        { Cpu.initialState with
            Registers =
                { Cpu.initialRegisters with
                    A = 0x20uy
                    B = 0x02uy
                    C = 0x02uy
                    D = 0x02uy
                    E = 0x02uy
                    H = 0x02uy
                    L = 0x02uy } }

    let result = Cpu.step cpu bus

    let expected = if opcode = 0x97uy then 0x00uy else 0x1Euy
    let expectedFlags = if opcode = 0x97uy then Cpu.ZeroFlag ||| Cpu.SubtractFlag else Cpu.SubtractFlag ||| Cpu.HalfCarryFlag
    Assert.Equal(expected, result.Cpu.Registers.A)
    Assert.Equal(expectedFlags, result.Cpu.Registers.F)
    Assert.Equal(0x0101us, result.Cpu.Registers.PC)
    Assert.Equal(4, result.Cycles)

[<Fact>]
let ``SUB A register sets half carry and carry on borrow`` () =
    let bus = makeBus [| 0x95uy |]
    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with A = 0x10uy; L = 0x21uy } }

    let result = Cpu.step cpu bus

    Assert.Equal(0xEFuy, result.Cpu.Registers.A)
    Assert.Equal(Cpu.SubtractFlag ||| Cpu.HalfCarryFlag ||| Cpu.CarryFlag, result.Cpu.Registers.F)
    Assert.Equal(0x0101us, result.Cpu.Registers.PC)
    Assert.Equal(4, result.Cycles)

[<Fact>]
let ``SUB HL subtracts memory from accumulator`` () =
    let bus =
        makeBus [| 0x96uy |]
        |> Bus.writeByte 0xC020us 0x21uy

    let cpu =
        { Cpu.initialState with
            Registers =
                { Cpu.initialRegisters with
                    A = 0x10uy
                    H = 0xC0uy
                    L = 0x20uy } }

    let result = Cpu.step cpu bus

    Assert.Equal(0xEFuy, result.Cpu.Registers.A)
    Assert.Equal(Cpu.SubtractFlag ||| Cpu.HalfCarryFlag ||| Cpu.CarryFlag, result.Cpu.Registers.F)
    Assert.Equal(0x0101us, result.Cpu.Registers.PC)
    Assert.Equal(8, result.Cycles)

[<Fact>]
let ``SUB d8 subtracts immediate from accumulator`` () =
    let bus = makeBus [| 0xD6uy; 0x21uy |]
    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with A = 0x10uy } }

    let result = Cpu.step cpu bus

    Assert.Equal(0xEFuy, result.Cpu.Registers.A)
    Assert.Equal(Cpu.SubtractFlag ||| Cpu.HalfCarryFlag ||| Cpu.CarryFlag, result.Cpu.Registers.F)
    Assert.Equal(0x0102us, result.Cpu.Registers.PC)
    Assert.Equal(8, result.Cycles)

[<Theory>]
[<InlineData(0x98uy)>]
[<InlineData(0x99uy)>]
[<InlineData(0x9Auy)>]
[<InlineData(0x9Buy)>]
[<InlineData(0x9Cuy)>]
[<InlineData(0x9Duy)>]
[<InlineData(0x9Fuy)>]
let ``SBC A register subtracts selected register and carry`` opcode =
    let bus = makeBus [| opcode |]
    let cpu =
        { Cpu.initialState with
            Registers =
                { Cpu.initialRegisters with
                    A = 0x20uy
                    B = 0x01uy
                    C = 0x01uy
                    D = 0x01uy
                    E = 0x01uy
                    H = 0x01uy
                    L = 0x01uy
                    F = Cpu.CarryFlag } }

    let result = Cpu.step cpu bus

    let expected = if opcode = 0x9Fuy then 0xFFuy else 0x1Euy
    let expectedFlags = if opcode = 0x9Fuy then Cpu.SubtractFlag ||| Cpu.HalfCarryFlag ||| Cpu.CarryFlag else Cpu.SubtractFlag ||| Cpu.HalfCarryFlag
    Assert.Equal(expected, result.Cpu.Registers.A)
    Assert.Equal(expectedFlags, result.Cpu.Registers.F)
    Assert.Equal(0x0101us, result.Cpu.Registers.PC)
    Assert.Equal(4, result.Cycles)

[<Fact>]
let ``SBC A HL subtracts memory and carry`` () =
    let bus =
        makeBus [| 0x9Euy |]
        |> Bus.writeByte 0xC020us 0x01uy

    let cpu =
        { Cpu.initialState with
            Registers =
                { Cpu.initialRegisters with
                    A = 0x20uy
                    H = 0xC0uy
                    L = 0x20uy
                    F = Cpu.CarryFlag } }

    let result = Cpu.step cpu bus

    Assert.Equal(0x1Euy, result.Cpu.Registers.A)
    Assert.Equal(Cpu.SubtractFlag ||| Cpu.HalfCarryFlag, result.Cpu.Registers.F)
    Assert.Equal(0x0101us, result.Cpu.Registers.PC)
    Assert.Equal(8, result.Cycles)

[<Fact>]
let ``SBC A d8 subtracts immediate and carry`` () =
    let bus = makeBus [| 0xDEuy; 0x01uy |]
    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with A = 0x20uy; F = Cpu.CarryFlag } }

    let result = Cpu.step cpu bus

    Assert.Equal(0x1Euy, result.Cpu.Registers.A)
    Assert.Equal(Cpu.SubtractFlag ||| Cpu.HalfCarryFlag, result.Cpu.Registers.F)
    Assert.Equal(0x0102us, result.Cpu.Registers.PC)
    Assert.Equal(8, result.Cycles)

[<Fact>]
let ``CP HL compares accumulator with memory and preserves A`` () =
    let bus =
        makeBus [| 0xBEuy |]
        |> Bus.writeByte 0xC000us 0x20uy

    let cpu =
        { Cpu.initialState with
            Registers =
                { Cpu.initialRegisters with
                    A = 0x20uy
                    H = 0xC0uy
                    L = 0x00uy } }

    let result = Cpu.step cpu bus

    Assert.Equal(0x20uy, result.Cpu.Registers.A)
    Assert.Equal(Cpu.ZeroFlag ||| Cpu.SubtractFlag, result.Cpu.Registers.F)
    Assert.Equal(0x0101us, result.Cpu.Registers.PC)
    Assert.Equal(8, result.Cycles)

[<Theory>]
[<InlineData(0xB8uy, "B")>]
[<InlineData(0xB9uy, "C")>]
[<InlineData(0xBAuy, "D")>]
[<InlineData(0xBBuy, "E")>]
[<InlineData(0xBCuy, "H")>]
[<InlineData(0xBDuy, "L")>]
[<InlineData(0xBFuy, "A")>]
let ``CP register compares accumulator with selected register`` opcode registerName =
    let bus = makeBus [| opcode |]
    let baseRegisters =
        { Cpu.initialRegisters with
            A = 0x20uy
            B = 0x20uy
            C = 0x20uy
            D = 0x20uy
            E = 0x20uy
            H = 0x20uy
            L = 0x20uy }

    let cpu = { Cpu.initialState with Registers = baseRegisters }
    let result = Cpu.step cpu bus

    Assert.Equal(0x20uy, result.Cpu.Registers.A)
    Assert.Equal(Cpu.ZeroFlag ||| Cpu.SubtractFlag, result.Cpu.Registers.F)
    Assert.Equal(0x0101us, result.Cpu.Registers.PC)
    Assert.Equal(4, result.Cycles)
    Assert.False(System.String.IsNullOrEmpty(registerName))

[<Fact>]
let ``CP register sets half carry and carry on borrow`` () =
    let bus = makeBus [| 0xB8uy |]
    let cpu =
        { Cpu.initialState with
            Registers =
                { Cpu.initialRegisters with
                    A = 0x10uy
                    B = 0x21uy } }

    let result = Cpu.step cpu bus

    Assert.Equal(0x10uy, result.Cpu.Registers.A)
    Assert.Equal(Cpu.SubtractFlag ||| Cpu.HalfCarryFlag ||| Cpu.CarryFlag, result.Cpu.Registers.F)
    Assert.Equal(0x0101us, result.Cpu.Registers.PC)
    Assert.Equal(4, result.Cycles)

[<Fact>]
let ``AND d8 updates A and flags`` () =
    let bus = makeBus [| 0xE6uy; 0x0Fuy |]
    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with A = 0xF0uy; F = Cpu.CarryFlag } }

    let result = Cpu.step cpu bus

    Assert.Equal(0uy, result.Cpu.Registers.A)
    Assert.Equal(Cpu.ZeroFlag ||| Cpu.HalfCarryFlag, result.Cpu.Registers.F)
    Assert.Equal(0x0102us, result.Cpu.Registers.PC)
    Assert.Equal(8, result.Cycles)

[<Fact>]
let ``AND C updates A and flags`` () =
    let bus = makeBus [| 0xA1uy |]
    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with A = 0xF3uy; C = 0x0Fuy; F = Cpu.CarryFlag } }

    let result = Cpu.step cpu bus

    Assert.Equal(0x03uy, result.Cpu.Registers.A)
    Assert.Equal(Cpu.HalfCarryFlag, result.Cpu.Registers.F)
    Assert.Equal(0x0101us, result.Cpu.Registers.PC)
    Assert.Equal(4, result.Cycles)

[<Fact>]
let ``AND B updates A and flags`` () =
    let bus = makeBus [| 0xA0uy |]
    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with A = 0xF3uy; B = 0x0Fuy; F = Cpu.CarryFlag } }

    let result = Cpu.step cpu bus

    Assert.Equal(0x03uy, result.Cpu.Registers.A)
    Assert.Equal(Cpu.HalfCarryFlag, result.Cpu.Registers.F)
    Assert.Equal(0x0101us, result.Cpu.Registers.PC)
    Assert.Equal(4, result.Cycles)

[<Fact>]
let ``AND D updates A and flags`` () =
    let bus = makeBus [| 0xA2uy |]
    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with A = 0xF3uy; D = 0x0Fuy; F = Cpu.CarryFlag } }

    let result = Cpu.step cpu bus

    Assert.Equal(0x03uy, result.Cpu.Registers.A)
    Assert.Equal(Cpu.HalfCarryFlag, result.Cpu.Registers.F)
    Assert.Equal(0x0101us, result.Cpu.Registers.PC)
    Assert.Equal(4, result.Cycles)

[<Fact>]
let ``AND E updates A and flags`` () =
    let bus = makeBus [| 0xA3uy |]
    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with A = 0xF3uy; E = 0x0Fuy; F = Cpu.CarryFlag } }

    let result = Cpu.step cpu bus

    Assert.Equal(0x03uy, result.Cpu.Registers.A)
    Assert.Equal(Cpu.HalfCarryFlag, result.Cpu.Registers.F)
    Assert.Equal(0x0101us, result.Cpu.Registers.PC)
    Assert.Equal(4, result.Cycles)

[<Fact>]
let ``AND H updates A and flags`` () =
    let bus = makeBus [| 0xA4uy |]
    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with A = 0xF3uy; H = 0x0Fuy; F = Cpu.CarryFlag } }

    let result = Cpu.step cpu bus

    Assert.Equal(0x03uy, result.Cpu.Registers.A)
    Assert.Equal(Cpu.HalfCarryFlag, result.Cpu.Registers.F)
    Assert.Equal(0x0101us, result.Cpu.Registers.PC)
    Assert.Equal(4, result.Cycles)

[<Fact>]
let ``AND HL updates A and flags`` () =
    let bus =
        makeBus [| 0xA6uy |]
        |> Bus.writeByte 0xC020us 0x0Fuy

    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with A = 0xF3uy; H = 0xC0uy; L = 0x20uy; F = Cpu.CarryFlag } }
    let result = Cpu.step cpu bus

    Assert.Equal(0x03uy, result.Cpu.Registers.A)
    Assert.Equal(Cpu.HalfCarryFlag, result.Cpu.Registers.F)
    Assert.Equal(0x0101us, result.Cpu.Registers.PC)
    Assert.Equal(8, result.Cycles)

[<Fact>]
let ``AND A updates flags and preserves A`` () =
    let bus = makeBus [| 0xA7uy |]
    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with A = 0x18uy; F = Cpu.CarryFlag } }

    let result = Cpu.step cpu bus

    Assert.Equal(0x18uy, result.Cpu.Registers.A)
    Assert.Equal(Cpu.HalfCarryFlag, result.Cpu.Registers.F)
    Assert.Equal(0x0101us, result.Cpu.Registers.PC)
    Assert.Equal(4, result.Cycles)

[<Fact>]
let ``OR C updates A and clears arithmetic flags`` () =
    let bus = makeBus [| 0xB1uy |]
    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with A = 0x10uy; C = 0x01uy; F = 0xF0uy } }

    let result = Cpu.step cpu bus

    Assert.Equal(0x11uy, result.Cpu.Registers.A)
    Assert.Equal(0uy, result.Cpu.Registers.F)
    Assert.Equal(0x0101us, result.Cpu.Registers.PC)
    Assert.Equal(4, result.Cycles)

[<Fact>]
let ``OR B updates A and clears arithmetic flags`` () =
    let bus = makeBus [| 0xB0uy |]
    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with A = 0x20uy; B = 0x02uy; F = 0xF0uy } }

    let result = Cpu.step cpu bus

    Assert.Equal(0x22uy, result.Cpu.Registers.A)
    Assert.Equal(0uy, result.Cpu.Registers.F)
    Assert.Equal(0x0101us, result.Cpu.Registers.PC)
    Assert.Equal(4, result.Cycles)

[<Fact>]
let ``OR E updates A and clears arithmetic flags`` () =
    let bus = makeBus [| 0xB3uy |]
    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with A = 0x20uy; E = 0x03uy; F = 0xF0uy } }

    let result = Cpu.step cpu bus

    Assert.Equal(0x23uy, result.Cpu.Registers.A)
    Assert.Equal(0uy, result.Cpu.Registers.F)
    Assert.Equal(0x0101us, result.Cpu.Registers.PC)
    Assert.Equal(4, result.Cycles)

[<Fact>]
let ``OR L updates A and clears arithmetic flags`` () =
    let bus = makeBus [| 0xB5uy |]
    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with A = 0x20uy; L = 0x05uy; F = 0xF0uy } }

    let result = Cpu.step cpu bus

    Assert.Equal(0x25uy, result.Cpu.Registers.A)
    Assert.Equal(0uy, result.Cpu.Registers.F)
    Assert.Equal(0x0101us, result.Cpu.Registers.PC)
    Assert.Equal(4, result.Cycles)

[<Fact>]
let ``OR HL updates A from memory and clears arithmetic flags`` () =
    let bus =
        makeBus [| 0xB6uy |]
        |> Bus.writeByte 0xC020us 0x06uy

    let cpu =
        { Cpu.initialState with
            Registers =
                { Cpu.initialRegisters with
                    A = 0x20uy
                    H = 0xC0uy
                    L = 0x20uy
                    F = 0xF0uy } }

    let result = Cpu.step cpu bus

    Assert.Equal(0x26uy, result.Cpu.Registers.A)
    Assert.Equal(0uy, result.Cpu.Registers.F)
    Assert.Equal(0x0101us, result.Cpu.Registers.PC)
    Assert.Equal(8, result.Cycles)

[<Fact>]
let ``OR d8 updates A with immediate and clears arithmetic flags`` () =
    let bus = makeBus [| 0xF6uy; 0x06uy |]
    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with A = 0x20uy; F = 0xF0uy } }

    let result = Cpu.step cpu bus

    Assert.Equal(0x26uy, result.Cpu.Registers.A)
    Assert.Equal(0uy, result.Cpu.Registers.F)
    Assert.Equal(0x0102us, result.Cpu.Registers.PC)
    Assert.Equal(8, result.Cycles)

[<Fact>]
let ``OR C sets zero flag when result is zero`` () =
    let bus = makeBus [| 0xB1uy |]
    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with A = 0uy; C = 0uy; F = Cpu.CarryFlag } }

    let result = Cpu.step cpu bus

    Assert.Equal(0uy, result.Cpu.Registers.A)
    Assert.Equal(Cpu.ZeroFlag, result.Cpu.Registers.F)

[<Fact>]
let ``OR D combines D into accumulator`` () =
    let bus = makeBus [| 0xB2uy |]
    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with A = 0x80uy; D = 0x02uy; F = Cpu.CarryFlag } }

    let result = Cpu.step cpu bus

    Assert.Equal(0x82uy, result.Cpu.Registers.A)
    Assert.Equal(0uy, result.Cpu.Registers.F)
    Assert.Equal(0x0101us, result.Cpu.Registers.PC)
    Assert.Equal(4, result.Cycles)

[<Fact>]
let ``OR A updates flags and preserves accumulator`` () =
    let bus = makeBus [| 0xB7uy |]
    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with A = 0uy; F = 0xF0uy } }

    let result = Cpu.step cpu bus

    Assert.Equal(0uy, result.Cpu.Registers.A)
    Assert.Equal(Cpu.ZeroFlag, result.Cpu.Registers.F)
    Assert.Equal(0x0101us, result.Cpu.Registers.PC)
    Assert.Equal(4, result.Cycles)

[<Fact>]
let ``CPL complements A and preserves zero and carry`` () =
    let bus = makeBus [| 0x2Fuy |]
    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with A = 0x0Fuy; F = Cpu.ZeroFlag ||| Cpu.CarryFlag } }

    let result = Cpu.step cpu bus

    Assert.Equal(0xF0uy, result.Cpu.Registers.A)
    Assert.Equal(Cpu.ZeroFlag ||| Cpu.SubtractFlag ||| Cpu.HalfCarryFlag ||| Cpu.CarryFlag, result.Cpu.Registers.F)
    Assert.Equal(0x0101us, result.Cpu.Registers.PC)
    Assert.Equal(4, result.Cycles)

[<Fact>]
let ``DAA adjusts accumulator after BCD addition`` () =
    let bus = makeBus [| 0x27uy |]
    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with A = 0x3Cuy; F = 0uy } }

    let result = Cpu.step cpu bus

    Assert.Equal(0x42uy, result.Cpu.Registers.A)
    Assert.Equal(0uy, result.Cpu.Registers.F)
    Assert.Equal(0x0101us, result.Cpu.Registers.PC)
    Assert.Equal(4, result.Cycles)

[<Fact>]
let ``DAA adjusts accumulator after BCD subtraction preserving subtract`` () =
    let bus = makeBus [| 0x27uy |]
    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with A = 0x0Fuy; F = Cpu.SubtractFlag ||| Cpu.HalfCarryFlag } }

    let result = Cpu.step cpu bus

    Assert.Equal(0x09uy, result.Cpu.Registers.A)
    Assert.Equal(Cpu.SubtractFlag, result.Cpu.Registers.F)
    Assert.Equal(0x0101us, result.Cpu.Registers.PC)
    Assert.Equal(4, result.Cycles)

[<Fact>]
let ``SCF sets carry and preserves zero`` () =
    let bus = makeBus [| 0x37uy |]
    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with F = Cpu.ZeroFlag ||| Cpu.SubtractFlag ||| Cpu.HalfCarryFlag } }

    let result = Cpu.step cpu bus

    Assert.Equal(Cpu.ZeroFlag ||| Cpu.CarryFlag, result.Cpu.Registers.F)
    Assert.Equal(0x0101us, result.Cpu.Registers.PC)
    Assert.Equal(4, result.Cycles)

[<Fact>]
let ``CCF complements carry and preserves zero`` () =
    let bus = makeBus [| 0x3Fuy |]
    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with F = Cpu.ZeroFlag ||| Cpu.CarryFlag ||| Cpu.SubtractFlag ||| Cpu.HalfCarryFlag } }

    let result = Cpu.step cpu bus

    Assert.Equal(Cpu.ZeroFlag, result.Cpu.Registers.F)
    Assert.Equal(0x0101us, result.Cpu.Registers.PC)
    Assert.Equal(4, result.Cycles)

[<Fact>]
let ``CCF sets carry when it was clear`` () =
    let bus = makeBus [| 0x3Fuy |]
    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with F = 0uy } }

    let result = Cpu.step cpu bus

    Assert.Equal(Cpu.CarryFlag, result.Cpu.Registers.F)
    Assert.Equal(0x0101us, result.Cpu.Registers.PC)
    Assert.Equal(4, result.Cycles)

[<Fact>]
let ``CB 87 resets bit zero of A`` () =
    let bus = makeBus [| 0xCBuy; 0x87uy |]
    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with A = 0xFFuy; F = Cpu.ZeroFlag } }

    let result = Cpu.step cpu bus

    Assert.Equal(0xFEuy, result.Cpu.Registers.A)
    Assert.Equal(Cpu.ZeroFlag, result.Cpu.Registers.F)
    Assert.Equal(0x0102us, result.Cpu.Registers.PC)
    Assert.Equal(8, result.Cycles)

[<Fact>]
let ``CB 8F resets bit one of A`` () =
    let bus = makeBus [| 0xCBuy; 0x8Fuy |]
    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with A = 0xFFuy; F = Cpu.CarryFlag } }

    let result = Cpu.step cpu bus

    Assert.Equal(0xFDuy, result.Cpu.Registers.A)
    Assert.Equal(Cpu.CarryFlag, result.Cpu.Registers.F)
    Assert.Equal(0x0102us, result.Cpu.Registers.PC)
    Assert.Equal(8, result.Cycles)

[<Fact>]
let ``CB 97 resets bit two of A`` () =
    let bus = makeBus [| 0xCBuy; 0x97uy |]
    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with A = 0xFFuy; F = Cpu.CarryFlag } }

    let result = Cpu.step cpu bus

    Assert.Equal(0xFBuy, result.Cpu.Registers.A)
    Assert.Equal(Cpu.CarryFlag, result.Cpu.Registers.F)
    Assert.Equal(0x0102us, result.Cpu.Registers.PC)
    Assert.Equal(8, result.Cycles)

[<Fact>]
let ``CB AF resets bit five of A`` () =
    let bus = makeBus [| 0xCBuy; 0xAFuy |]
    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with A = 0xFFuy; F = Cpu.CarryFlag } }

    let result = Cpu.step cpu bus

    Assert.Equal(0xDFuy, result.Cpu.Registers.A)
    Assert.Equal(Cpu.CarryFlag, result.Cpu.Registers.F)
    Assert.Equal(0x0102us, result.Cpu.Registers.PC)
    Assert.Equal(8, result.Cycles)

[<Fact>]
let ``CB 9E resets bit three through HL`` () =
    let bus =
        makeBus [| 0xCBuy; 0x9Euy |]
        |> Bus.writeByte 0xC020us 0xFFuy

    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with H = 0xC0uy; L = 0x20uy } }
    let result = Cpu.step cpu bus

    Assert.Equal(0xF7uy, Bus.readByte 0xC020us result.Bus)
    Assert.Equal(0x0102us, result.Cpu.Registers.PC)
    Assert.Equal(16, result.Cycles)

[<Fact>]
let ``CB A6 resets bit four through HL`` () =
    let bus =
        makeBus [| 0xCBuy; 0xA6uy |]
        |> Bus.writeByte 0xC020us 0xFFuy

    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with H = 0xC0uy; L = 0x20uy } }
    let result = Cpu.step cpu bus

    Assert.Equal(0xEFuy, Bus.readByte 0xC020us result.Bus)
    Assert.Equal(0x0102us, result.Cpu.Registers.PC)
    Assert.Equal(16, result.Cycles)

[<Fact>]
let ``CB AE resets bit five through HL`` () =
    let bus =
        makeBus [| 0xCBuy; 0xAEuy |]
        |> Bus.writeByte 0xC020us 0xFFuy

    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with H = 0xC0uy; L = 0x20uy } }
    let result = Cpu.step cpu bus

    Assert.Equal(0xDFuy, Bus.readByte 0xC020us result.Bus)
    Assert.Equal(0x0102us, result.Cpu.Registers.PC)
    Assert.Equal(16, result.Cycles)

[<Fact>]
let ``CB B6 resets bit six through HL`` () =
    let bus =
        makeBus [| 0xCBuy; 0xB6uy |]
        |> Bus.writeByte 0xC020us 0xFFuy

    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with H = 0xC0uy; L = 0x20uy } }
    let result = Cpu.step cpu bus

    Assert.Equal(0xBFuy, Bus.readByte 0xC020us result.Bus)
    Assert.Equal(0x0102us, result.Cpu.Registers.PC)
    Assert.Equal(16, result.Cycles)

[<Fact>]
let ``CB DE sets bit three through HL`` () =
    let bus =
        makeBus [| 0xCBuy; 0xDEuy |]
        |> Bus.writeByte 0xC020us 0x00uy

    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with H = 0xC0uy; L = 0x20uy } }
    let result = Cpu.step cpu bus

    Assert.Equal(0x08uy, Bus.readByte 0xC020us result.Bus)
    Assert.Equal(0x0102us, result.Cpu.Registers.PC)
    Assert.Equal(16, result.Cycles)

[<Fact>]
let ``CB 96 resets bit two through HL`` () =
    let bus =
        makeBus [| 0xCBuy; 0x96uy |]
        |> Bus.writeByte 0xC020us 0xFFuy

    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with H = 0xC0uy; L = 0x20uy } }
    let result = Cpu.step cpu bus

    Assert.Equal(0xFBuy, Bus.readByte 0xC020us result.Bus)
    Assert.Equal(0x0102us, result.Cpu.Registers.PC)
    Assert.Equal(16, result.Cycles)

[<Fact>]
let ``CB 86 resets bit zero through HL`` () =
    let bus =
        makeBus [| 0xCBuy; 0x86uy |]
        |> Bus.writeByte 0xC020us 0xFFuy

    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with H = 0xC0uy; L = 0x20uy } }
    let result = Cpu.step cpu bus

    Assert.Equal(0xFEuy, Bus.readByte 0xC020us result.Bus)
    Assert.Equal(0x0102us, result.Cpu.Registers.PC)
    Assert.Equal(16, result.Cycles)

[<Fact>]
let ``CB 8E resets bit one through HL`` () =
    let bus =
        makeBus [| 0xCBuy; 0x8Euy |]
        |> Bus.writeByte 0xC020us 0xFFuy

    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with H = 0xC0uy; L = 0x20uy } }
    let result = Cpu.step cpu bus

    Assert.Equal(0xFDuy, Bus.readByte 0xC020us result.Bus)
    Assert.Equal(0x0102us, result.Cpu.Registers.PC)
    Assert.Equal(16, result.Cycles)

[<Fact>]
let ``CB E6 sets bit four through HL`` () =
    let bus =
        makeBus [| 0xCBuy; 0xE6uy |]
        |> Bus.writeByte 0xC020us 0x00uy

    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with H = 0xC0uy; L = 0x20uy } }
    let result = Cpu.step cpu bus

    Assert.Equal(0x10uy, Bus.readByte 0xC020us result.Bus)
    Assert.Equal(0x0102us, result.Cpu.Registers.PC)
    Assert.Equal(16, result.Cycles)

[<Fact>]
let ``CB FF sets bit seven of A`` () =
    let bus = makeBus [| 0xCBuy; 0xFFuy |]
    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with A = 0x01uy; F = Cpu.ZeroFlag } }

    let result = Cpu.step cpu bus

    Assert.Equal(0x81uy, result.Cpu.Registers.A)
    Assert.Equal(Cpu.ZeroFlag, result.Cpu.Registers.F)
    Assert.Equal(0x0102us, result.Cpu.Registers.PC)
    Assert.Equal(8, result.Cycles)

[<Fact>]
let ``CB D6 sets bit two through HL`` () =
    let bus =
        makeBus [| 0xCBuy; 0xD6uy |]
        |> Bus.writeByte 0xC020us 0x00uy

    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with H = 0xC0uy; L = 0x20uy } }
    let result = Cpu.step cpu bus

    Assert.Equal(0x04uy, Bus.readByte 0xC020us result.Bus)
    Assert.Equal(0x0102us, result.Cpu.Registers.PC)
    Assert.Equal(16, result.Cycles)

[<Fact>]
let ``CB CF sets bit one of A`` () =
    let bus = makeBus [| 0xCBuy; 0xCFuy |]
    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with A = 0x80uy; F = Cpu.CarryFlag } }

    let result = Cpu.step cpu bus

    Assert.Equal(0x82uy, result.Cpu.Registers.A)
    Assert.Equal(Cpu.CarryFlag, result.Cpu.Registers.F)
    Assert.Equal(0x0102us, result.Cpu.Registers.PC)
    Assert.Equal(8, result.Cycles)

[<Fact>]
let ``CB CE sets bit one through HL`` () =
    let bus =
        makeBus [| 0xCBuy; 0xCEuy |]
        |> Bus.writeByte 0xC020us 0x00uy

    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with H = 0xC0uy; L = 0x20uy } }
    let result = Cpu.step cpu bus

    Assert.Equal(0x02uy, Bus.readByte 0xC020us result.Bus)
    Assert.Equal(0x0102us, result.Cpu.Registers.PC)
    Assert.Equal(16, result.Cycles)

[<Fact>]
let ``CB C6 sets bit zero through HL`` () =
    let bus =
        makeBus [| 0xCBuy; 0xC6uy |]
        |> Bus.writeByte 0xC020us 0x00uy

    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with H = 0xC0uy; L = 0x20uy } }
    let result = Cpu.step cpu bus

    Assert.Equal(0x01uy, Bus.readByte 0xC020us result.Bus)
    Assert.Equal(0x0102us, result.Cpu.Registers.PC)
    Assert.Equal(16, result.Cycles)

[<Fact>]
let ``CB 37 swaps nibbles of A and updates flags`` () =
    let bus = makeBus [| 0xCBuy; 0x37uy |]
    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with A = 0xF0uy; F = 0xF0uy } }

    let result = Cpu.step cpu bus

    Assert.Equal(0x0Fuy, result.Cpu.Registers.A)
    Assert.Equal(0uy, result.Cpu.Registers.F)
    Assert.Equal(0x0102us, result.Cpu.Registers.PC)
    Assert.Equal(8, result.Cycles)

[<Fact>]
let ``CB 37 sets zero flag when swapped A is zero`` () =
    let bus = makeBus [| 0xCBuy; 0x37uy |]
    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with A = 0uy; F = Cpu.CarryFlag } }

    let result = Cpu.step cpu bus

    Assert.Equal(0uy, result.Cpu.Registers.A)
    Assert.Equal(Cpu.ZeroFlag, result.Cpu.Registers.F)

[<Fact>]
let ``CB 33 swaps nibbles of E and updates flags`` () =
    let bus = makeBus [| 0xCBuy; 0x33uy |]
    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with E = 0xF0uy; F = Cpu.CarryFlag } }

    let result = Cpu.step cpu bus

    Assert.Equal(0x0Fuy, result.Cpu.Registers.E)
    Assert.Equal(0uy, result.Cpu.Registers.F)
    Assert.Equal(0x0102us, result.Cpu.Registers.PC)
    Assert.Equal(8, result.Cycles)

[<Fact>]
let ``CB 36 swaps nibbles through HL`` () =
    let bus =
        makeBus [| 0xCBuy; 0x36uy |]
        |> Bus.writeByte 0xC020us 0xF0uy

    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with H = 0xC0uy; L = 0x20uy; F = Cpu.CarryFlag } }
    let result = Cpu.step cpu bus

    Assert.Equal(0x0Fuy, Bus.readByte 0xC020us result.Bus)
    Assert.Equal(0uy, result.Cpu.Registers.F)
    Assert.Equal(0x0102us, result.Cpu.Registers.PC)
    Assert.Equal(16, result.Cycles)

[<Fact>]
let ``CB 12 rotates D left through carry`` () =
    let bus = makeBus [| 0xCBuy; 0x12uy |]
    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with D = 0x80uy; F = Cpu.CarryFlag } }

    let result = Cpu.step cpu bus

    Assert.Equal(0x01uy, result.Cpu.Registers.D)
    Assert.Equal(Cpu.CarryFlag, result.Cpu.Registers.F)
    Assert.Equal(0x0102us, result.Cpu.Registers.PC)
    Assert.Equal(8, result.Cycles)

[<Fact>]
let ``CB 0E rotates HL memory right circular`` () =
    let bus =
        makeBus [| 0xCBuy; 0x0Euy |]
        |> Bus.writeByte 0xC020us 0x01uy

    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with H = 0xC0uy; L = 0x20uy; F = Cpu.ZeroFlag } }
    let result = Cpu.step cpu bus

    Assert.Equal(0x80uy, Bus.readByte 0xC020us result.Bus)
    Assert.Equal(Cpu.CarryFlag, result.Cpu.Registers.F)
    Assert.Equal(0x0102us, result.Cpu.Registers.PC)
    Assert.Equal(16, result.Cycles)

[<Fact>]
let ``CB 0B rotates E right circular`` () =
    let bus = makeBus [| 0xCBuy; 0x0Buy |]
    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with E = 0x01uy; F = Cpu.ZeroFlag } }

    let result = Cpu.step cpu bus

    Assert.Equal(0x80uy, result.Cpu.Registers.E)
    Assert.Equal(Cpu.CarryFlag, result.Cpu.Registers.F)
    Assert.Equal(0x0102us, result.Cpu.Registers.PC)
    Assert.Equal(8, result.Cycles)

[<Theory>]
[<InlineData(0x18uy, "B")>]
[<InlineData(0x19uy, "C")>]
[<InlineData(0x1Auy, "D")>]
[<InlineData(0x1Buy, "E")>]
[<InlineData(0x1Cuy, "H")>]
[<InlineData(0x1Duy, "L")>]
[<InlineData(0x1Fuy, "A")>]
let ``CB RR register rotates selected register right through carry`` opcode registerName =
    let bus = makeBus [| 0xCBuy; opcode |]
    let cpu =
        { Cpu.initialState with
            Registers =
                { Cpu.initialRegisters with
                    A = 0x03uy
                    B = 0x03uy
                    C = 0x03uy
                    D = 0x03uy
                    E = 0x03uy
                    H = 0x03uy
                    L = 0x03uy
                    F = Cpu.CarryFlag } }

    let result = Cpu.step cpu bus

    match registerName with
    | "A" -> Assert.Equal(0x81uy, result.Cpu.Registers.A)
    | "B" -> Assert.Equal(0x81uy, result.Cpu.Registers.B)
    | "C" -> Assert.Equal(0x81uy, result.Cpu.Registers.C)
    | "D" -> Assert.Equal(0x81uy, result.Cpu.Registers.D)
    | "E" -> Assert.Equal(0x81uy, result.Cpu.Registers.E)
    | "H" -> Assert.Equal(0x81uy, result.Cpu.Registers.H)
    | "L" -> Assert.Equal(0x81uy, result.Cpu.Registers.L)
    | other -> Assert.Fail $"Unexpected register: {other}"

    Assert.Equal(Cpu.CarryFlag, result.Cpu.Registers.F)
    Assert.Equal(0x0102us, result.Cpu.Registers.PC)
    Assert.Equal(8, result.Cycles)

[<Fact>]
let ``CB RR register sets zero when result is zero`` () =
    let bus = makeBus [| 0xCBuy; 0x18uy |]
    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with B = 0x00uy; F = 0uy } }

    let result = Cpu.step cpu bus

    Assert.Equal(0x00uy, result.Cpu.Registers.B)
    Assert.Equal(Cpu.ZeroFlag, result.Cpu.Registers.F)
    Assert.Equal(0x0102us, result.Cpu.Registers.PC)
    Assert.Equal(8, result.Cycles)

[<Fact>]
let ``CB 27 shifts A left arithmetic`` () =
    let bus = makeBus [| 0xCBuy; 0x27uy |]
    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with A = 0x80uy; F = Cpu.ZeroFlag } }

    let result = Cpu.step cpu bus

    Assert.Equal(0x00uy, result.Cpu.Registers.A)
    Assert.Equal(Cpu.ZeroFlag ||| Cpu.CarryFlag, result.Cpu.Registers.F)
    Assert.Equal(0x0102us, result.Cpu.Registers.PC)
    Assert.Equal(8, result.Cycles)

[<Fact>]
let ``CB 23 shifts E left arithmetic`` () =
    let bus = makeBus [| 0xCBuy; 0x23uy |]
    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with E = 0x81uy; F = Cpu.ZeroFlag } }

    let result = Cpu.step cpu bus

    Assert.Equal(0x02uy, result.Cpu.Registers.E)
    Assert.Equal(Cpu.CarryFlag, result.Cpu.Registers.F)
    Assert.Equal(0x0102us, result.Cpu.Registers.PC)
    Assert.Equal(8, result.Cycles)

[<Fact>]
let ``CB 21 shifts C left arithmetic`` () =
    let bus = makeBus [| 0xCBuy; 0x21uy |]
    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with C = 0x81uy; F = Cpu.ZeroFlag } }

    let result = Cpu.step cpu bus

    Assert.Equal(0x02uy, result.Cpu.Registers.C)
    Assert.Equal(Cpu.CarryFlag, result.Cpu.Registers.F)
    Assert.Equal(0x0102us, result.Cpu.Registers.PC)
    Assert.Equal(8, result.Cycles)

[<Fact>]
let ``CB 2A shifts D right arithmetic`` () =
    let bus = makeBus [| 0xCBuy; 0x2Auy |]
    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with D = 0x81uy; F = Cpu.ZeroFlag } }

    let result = Cpu.step cpu bus

    Assert.Equal(0xC0uy, result.Cpu.Registers.D)
    Assert.Equal(Cpu.CarryFlag, result.Cpu.Registers.F)
    Assert.Equal(0x0102us, result.Cpu.Registers.PC)
    Assert.Equal(8, result.Cycles)

[<Fact>]
let ``CB 3F shifts A right logical and moves bit zero to carry`` () =
    let bus = makeBus [| 0xCBuy; 0x3Fuy |]
    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with A = 0x03uy; F = Cpu.ZeroFlag } }

    let result = Cpu.step cpu bus

    Assert.Equal(0x01uy, result.Cpu.Registers.A)
    Assert.Equal(Cpu.CarryFlag, result.Cpu.Registers.F)
    Assert.Equal(0x0102us, result.Cpu.Registers.PC)
    Assert.Equal(8, result.Cycles)

[<Fact>]
let ``CB 3F sets zero when shifted A becomes zero`` () =
    let bus = makeBus [| 0xCBuy; 0x3Fuy |]
    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with A = 0x00uy; F = Cpu.CarryFlag } }

    let result = Cpu.step cpu bus

    Assert.Equal(0x00uy, result.Cpu.Registers.A)
    Assert.Equal(Cpu.ZeroFlag, result.Cpu.Registers.F)
    Assert.Equal(0x0102us, result.Cpu.Registers.PC)
    Assert.Equal(8, result.Cycles)

[<Theory>]
[<InlineData(0x38uy, "B")>]
[<InlineData(0x39uy, "C")>]
[<InlineData(0x3Auy, "D")>]
[<InlineData(0x3Buy, "E")>]
[<InlineData(0x3Cuy, "H")>]
[<InlineData(0x3Duy, "L")>]
let ``CB SRL register shifts selected register right`` opcode registerName =
    let bus = makeBus [| 0xCBuy; opcode |]
    let cpu =
        { Cpu.initialState with
            Registers =
                { Cpu.initialRegisters with
                    B = 0x03uy
                    C = 0x03uy
                    D = 0x03uy
                    E = 0x03uy
                    H = 0x03uy
                    L = 0x03uy } }

    let result = Cpu.step cpu bus

    match registerName with
    | "B" -> Assert.Equal(0x01uy, result.Cpu.Registers.B)
    | "C" -> Assert.Equal(0x01uy, result.Cpu.Registers.C)
    | "D" -> Assert.Equal(0x01uy, result.Cpu.Registers.D)
    | "E" -> Assert.Equal(0x01uy, result.Cpu.Registers.E)
    | "H" -> Assert.Equal(0x01uy, result.Cpu.Registers.H)
    | "L" -> Assert.Equal(0x01uy, result.Cpu.Registers.L)
    | other -> Assert.Fail $"Unexpected register: {other}"

    Assert.Equal(Cpu.CarryFlag, result.Cpu.Registers.F)
    Assert.Equal(0x0102us, result.Cpu.Registers.PC)
    Assert.Equal(8, result.Cycles)

[<Fact>]
let ``CB 47 tests bit zero of A and preserves carry`` () =
    let bus = makeBus [| 0xCBuy; 0x47uy |]
    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with A = 0x02uy; F = Cpu.CarryFlag ||| Cpu.SubtractFlag } }

    let result = Cpu.step cpu bus

    Assert.Equal(0x02uy, result.Cpu.Registers.A)
    Assert.Equal(Cpu.ZeroFlag ||| Cpu.HalfCarryFlag ||| Cpu.CarryFlag, result.Cpu.Registers.F)
    Assert.Equal(0x0102us, result.Cpu.Registers.PC)
    Assert.Equal(8, result.Cycles)

[<Fact>]
let ``CB 40 tests bit zero of B`` () =
    let bus = makeBus [| 0xCBuy; 0x40uy |]
    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with B = 0x01uy; F = Cpu.CarryFlag } }

    let result = Cpu.step cpu bus

    Assert.Equal(Cpu.HalfCarryFlag ||| Cpu.CarryFlag, result.Cpu.Registers.F)
    Assert.Equal(0x0102us, result.Cpu.Registers.PC)
    Assert.Equal(8, result.Cycles)

[<Fact>]
let ``CB 41 tests bit zero of C`` () =
    let bus = makeBus [| 0xCBuy; 0x41uy |]
    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with C = 0x01uy; F = Cpu.CarryFlag } }

    let result = Cpu.step cpu bus

    Assert.Equal(Cpu.HalfCarryFlag ||| Cpu.CarryFlag, result.Cpu.Registers.F)
    Assert.Equal(0x0102us, result.Cpu.Registers.PC)
    Assert.Equal(8, result.Cycles)

[<Fact>]
let ``CB 42 tests bit zero of D`` () =
    let bus = makeBus [| 0xCBuy; 0x42uy |]
    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with D = 0x00uy; F = Cpu.CarryFlag } }

    let result = Cpu.step cpu bus

    Assert.Equal(Cpu.ZeroFlag ||| Cpu.HalfCarryFlag ||| Cpu.CarryFlag, result.Cpu.Registers.F)
    Assert.Equal(0x0102us, result.Cpu.Registers.PC)
    Assert.Equal(8, result.Cycles)

[<Fact>]
let ``CB 43 tests bit zero of E`` () =
    let bus = makeBus [| 0xCBuy; 0x43uy |]
    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with E = 0x01uy; F = Cpu.CarryFlag } }

    let result = Cpu.step cpu bus

    Assert.Equal(Cpu.HalfCarryFlag ||| Cpu.CarryFlag, result.Cpu.Registers.F)
    Assert.Equal(0x0102us, result.Cpu.Registers.PC)
    Assert.Equal(8, result.Cycles)

[<Fact>]
let ``CB 46 tests bit zero through HL`` () =
    let bus =
        makeBus [| 0xCBuy; 0x46uy |]
        |> Bus.writeByte 0xC020us 0x01uy

    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with H = 0xC0uy; L = 0x20uy; F = Cpu.CarryFlag } }
    let result = Cpu.step cpu bus

    Assert.Equal(Cpu.HalfCarryFlag ||| Cpu.CarryFlag, result.Cpu.Registers.F)
    Assert.Equal(0x0102us, result.Cpu.Registers.PC)
    Assert.Equal(12, result.Cycles)

[<Fact>]
let ``CB 47 clears zero when bit zero of A is set`` () =
    let bus = makeBus [| 0xCBuy; 0x47uy |]
    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with A = 0x01uy; F = 0uy } }

    let result = Cpu.step cpu bus

    Assert.Equal(0x01uy, result.Cpu.Registers.A)
    Assert.Equal(Cpu.HalfCarryFlag, result.Cpu.Registers.F)
    Assert.Equal(0x0102us, result.Cpu.Registers.PC)
    Assert.Equal(8, result.Cycles)

[<Fact>]
let ``CB 4F tests bit one of A`` () =
    let bus = makeBus [| 0xCBuy; 0x4Fuy |]
    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with A = 0x02uy; F = Cpu.CarryFlag } }

    let result = Cpu.step cpu bus

    Assert.Equal(Cpu.HalfCarryFlag ||| Cpu.CarryFlag, result.Cpu.Registers.F)
    Assert.Equal(0x0102us, result.Cpu.Registers.PC)
    Assert.Equal(8, result.Cycles)

[<Fact>]
let ``CB 48 tests bit one of B`` () =
    let bus = makeBus [| 0xCBuy; 0x48uy |]
    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with B = 0x02uy; F = Cpu.CarryFlag } }

    let result = Cpu.step cpu bus

    Assert.Equal(Cpu.HalfCarryFlag ||| Cpu.CarryFlag, result.Cpu.Registers.F)
    Assert.Equal(0x0102us, result.Cpu.Registers.PC)
    Assert.Equal(8, result.Cycles)

[<Fact>]
let ``CB 50 tests bit two of B`` () =
    let bus = makeBus [| 0xCBuy; 0x50uy |]
    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with B = 0x00uy; F = Cpu.CarryFlag } }

    let result = Cpu.step cpu bus

    Assert.Equal(Cpu.ZeroFlag ||| Cpu.HalfCarryFlag ||| Cpu.CarryFlag, result.Cpu.Registers.F)
    Assert.Equal(0x0102us, result.Cpu.Registers.PC)
    Assert.Equal(8, result.Cycles)

[<Fact>]
let ``CB 4E tests bit one through HL`` () =
    let bus =
        makeBus [| 0xCBuy; 0x4Euy |]
        |> Bus.writeByte 0xC020us 0x00uy

    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with H = 0xC0uy; L = 0x20uy; F = Cpu.CarryFlag } }
    let result = Cpu.step cpu bus

    Assert.Equal(Cpu.ZeroFlag ||| Cpu.HalfCarryFlag ||| Cpu.CarryFlag, result.Cpu.Registers.F)
    Assert.Equal(0x0102us, result.Cpu.Registers.PC)
    Assert.Equal(12, result.Cycles)

[<Fact>]
let ``CB 56 tests bit two through HL`` () =
    let bus =
        makeBus [| 0xCBuy; 0x56uy |]
        |> Bus.writeByte 0xC020us 0x04uy

    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with H = 0xC0uy; L = 0x20uy; F = Cpu.CarryFlag } }
    let result = Cpu.step cpu bus

    Assert.Equal(Cpu.HalfCarryFlag ||| Cpu.CarryFlag, result.Cpu.Registers.F)
    Assert.Equal(0x0102us, result.Cpu.Registers.PC)
    Assert.Equal(12, result.Cycles)

[<Fact>]
let ``CB 57 tests bit two of A`` () =
    let bus = makeBus [| 0xCBuy; 0x57uy |]
    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with A = 0x04uy; F = Cpu.CarryFlag } }

    let result = Cpu.step cpu bus

    Assert.Equal(0x04uy, result.Cpu.Registers.A)
    Assert.Equal(Cpu.HalfCarryFlag ||| Cpu.CarryFlag, result.Cpu.Registers.F)
    Assert.Equal(0x0102us, result.Cpu.Registers.PC)
    Assert.Equal(8, result.Cycles)

[<Fact>]
let ``CB 58 tests bit three of B`` () =
    let bus = makeBus [| 0xCBuy; 0x58uy |]
    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with B = 0x08uy; F = Cpu.CarryFlag } }

    let result = Cpu.step cpu bus

    Assert.Equal(Cpu.HalfCarryFlag ||| Cpu.CarryFlag, result.Cpu.Registers.F)
    Assert.Equal(0x0102us, result.Cpu.Registers.PC)
    Assert.Equal(8, result.Cycles)

[<Fact>]
let ``CB 5E tests bit three through HL`` () =
    let bus =
        makeBus [| 0xCBuy; 0x5Euy |]
        |> Bus.writeByte 0xC020us 0x08uy

    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with H = 0xC0uy; L = 0x20uy; F = Cpu.CarryFlag } }
    let result = Cpu.step cpu bus

    Assert.Equal(Cpu.HalfCarryFlag ||| Cpu.CarryFlag, result.Cpu.Registers.F)
    Assert.Equal(0x0102us, result.Cpu.Registers.PC)
    Assert.Equal(12, result.Cycles)

[<Fact>]
let ``CB 5F tests bit three of A`` () =
    let bus = makeBus [| 0xCBuy; 0x5Fuy |]
    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with A = 0x08uy; F = Cpu.CarryFlag } }

    let result = Cpu.step cpu bus

    Assert.Equal(Cpu.HalfCarryFlag ||| Cpu.CarryFlag, result.Cpu.Registers.F)
    Assert.Equal(0x0102us, result.Cpu.Registers.PC)
    Assert.Equal(8, result.Cycles)

[<Fact>]
let ``CB 60 tests bit four of B`` () =
    let bus = makeBus [| 0xCBuy; 0x60uy |]
    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with B = 0x10uy; F = Cpu.CarryFlag } }

    let result = Cpu.step cpu bus

    Assert.Equal(Cpu.HalfCarryFlag ||| Cpu.CarryFlag, result.Cpu.Registers.F)
    Assert.Equal(0x0102us, result.Cpu.Registers.PC)
    Assert.Equal(8, result.Cycles)

[<Fact>]
let ``CB 61 tests bit four of C`` () =
    let bus = makeBus [| 0xCBuy; 0x61uy |]
    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with C = 0x10uy; F = Cpu.CarryFlag } }

    let result = Cpu.step cpu bus

    Assert.Equal(Cpu.HalfCarryFlag ||| Cpu.CarryFlag, result.Cpu.Registers.F)
    Assert.Equal(0x0102us, result.Cpu.Registers.PC)
    Assert.Equal(8, result.Cycles)

[<Fact>]
let ``CB 66 tests bit four through HL`` () =
    let bus =
        makeBus [| 0xCBuy; 0x66uy |]
        |> Bus.writeByte 0xC020us 0x10uy

    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with H = 0xC0uy; L = 0x20uy; F = Cpu.CarryFlag } }
    let result = Cpu.step cpu bus

    Assert.Equal(Cpu.HalfCarryFlag ||| Cpu.CarryFlag, result.Cpu.Registers.F)
    Assert.Equal(0x0102us, result.Cpu.Registers.PC)
    Assert.Equal(12, result.Cycles)

[<Fact>]
let ``CB 68 tests bit five of B`` () =
    let bus = makeBus [| 0xCBuy; 0x68uy |]
    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with B = 0x00uy; F = Cpu.CarryFlag } }

    let result = Cpu.step cpu bus

    Assert.Equal(Cpu.ZeroFlag ||| Cpu.HalfCarryFlag ||| Cpu.CarryFlag, result.Cpu.Registers.F)
    Assert.Equal(0x0102us, result.Cpu.Registers.PC)
    Assert.Equal(8, result.Cycles)

[<Fact>]
let ``CB 69 tests bit five of C`` () =
    let bus = makeBus [| 0xCBuy; 0x69uy |]
    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with C = 0x20uy; F = Cpu.CarryFlag } }

    let result = Cpu.step cpu bus

    Assert.Equal(Cpu.HalfCarryFlag ||| Cpu.CarryFlag, result.Cpu.Registers.F)
    Assert.Equal(0x0102us, result.Cpu.Registers.PC)
    Assert.Equal(8, result.Cycles)

[<Fact>]
let ``CB 6F tests bit five of A`` () =
    let bus = makeBus [| 0xCBuy; 0x6Fuy |]
    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with A = 0x20uy; F = Cpu.CarryFlag } }

    let result = Cpu.step cpu bus

    Assert.Equal(Cpu.HalfCarryFlag ||| Cpu.CarryFlag, result.Cpu.Registers.F)
    Assert.Equal(0x0102us, result.Cpu.Registers.PC)
    Assert.Equal(8, result.Cycles)

[<Fact>]
let ``CB 6E tests bit five through HL`` () =
    let bus =
        makeBus [| 0xCBuy; 0x6Euy |]
        |> Bus.writeByte 0xC020us 0x20uy

    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with H = 0xC0uy; L = 0x20uy; F = Cpu.CarryFlag } }
    let result = Cpu.step cpu bus

    Assert.Equal(Cpu.HalfCarryFlag ||| Cpu.CarryFlag, result.Cpu.Registers.F)
    Assert.Equal(0x0102us, result.Cpu.Registers.PC)
    Assert.Equal(12, result.Cycles)

[<Fact>]
let ``CB 77 tests bit six of A`` () =
    let bus = makeBus [| 0xCBuy; 0x77uy |]
    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with A = 0x40uy; F = Cpu.CarryFlag } }

    let result = Cpu.step cpu bus

    Assert.Equal(Cpu.HalfCarryFlag ||| Cpu.CarryFlag, result.Cpu.Registers.F)
    Assert.Equal(0x0102us, result.Cpu.Registers.PC)
    Assert.Equal(8, result.Cycles)

[<Fact>]
let ``CB 76 tests bit six through HL`` () =
    let bus =
        makeBus [| 0xCBuy; 0x76uy |]
        |> Bus.writeByte 0xC020us 0x40uy

    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with H = 0xC0uy; L = 0x20uy; F = Cpu.CarryFlag } }
    let result = Cpu.step cpu bus

    Assert.Equal(Cpu.HalfCarryFlag ||| Cpu.CarryFlag, result.Cpu.Registers.F)
    Assert.Equal(0x0102us, result.Cpu.Registers.PC)
    Assert.Equal(12, result.Cycles)

[<Fact>]
let ``CB 7F tests bit seven of A`` () =
    let bus = makeBus [| 0xCBuy; 0x7Fuy |]
    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with A = 0x7Fuy; F = Cpu.CarryFlag } }

    let result = Cpu.step cpu bus

    Assert.Equal(Cpu.ZeroFlag ||| Cpu.HalfCarryFlag ||| Cpu.CarryFlag, result.Cpu.Registers.F)
    Assert.Equal(0x0102us, result.Cpu.Registers.PC)
    Assert.Equal(8, result.Cycles)

[<Fact>]
let ``CB 7E tests bit seven through HL`` () =
    let bus =
        makeBus [| 0xCBuy; 0x7Euy |]
        |> Bus.writeByte 0xC020us 0x80uy

    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with H = 0xC0uy; L = 0x20uy; F = Cpu.CarryFlag } }
    let result = Cpu.step cpu bus

    Assert.Equal(Cpu.HalfCarryFlag ||| Cpu.CarryFlag, result.Cpu.Registers.F)
    Assert.Equal(0x0102us, result.Cpu.Registers.PC)
    Assert.Equal(12, result.Cycles)
