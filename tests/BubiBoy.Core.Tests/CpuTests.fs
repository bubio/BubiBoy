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
let ``JP a16 sets PC to immediate target`` () =
    let bus = makeBus [| 0xC3uy; 0x00uy; 0x20uy |]
    let result = Cpu.step Cpu.initialState bus

    Assert.Equal(0x2000us, result.Cpu.Registers.PC)
    Assert.Equal(16, result.Cycles)

[<Fact>]
let ``XOR A clears A and sets zero flag`` () =
    let bus = makeBus [| 0xAFuy |]
    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with A = 0x42uy; F = 0x10uy } }
    let result = Cpu.step cpu bus

    Assert.Equal(0uy, result.Cpu.Registers.A)
    Assert.Equal(Cpu.ZeroFlag, result.Cpu.Registers.F)
    Assert.Equal(0x0101us, result.Cpu.Registers.PC)

[<Fact>]
let ``LDH a8 A stores A in high IO page`` () =
    let bus = makeBus [| 0xE0uy; 0x50uy |]
    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with A = 0x91uy } }
    let result = Cpu.step cpu bus

    Assert.Equal(0x91uy, Bus.readByte 0xFF50us result.Bus)
    Assert.Equal(0x0102us, result.Cpu.Registers.PC)

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
let ``LDH A a8 reads from high IO page`` () =
    let bus =
        makeBus [| 0xF0uy; 0x80uy |]
        |> Bus.writeByte 0xFF80us 0x5Auy

    let result = Cpu.step Cpu.initialState bus

    Assert.Equal(0x5Auy, result.Cpu.Registers.A)
    Assert.Equal(0x0102us, result.Cpu.Registers.PC)
    Assert.Equal(12, result.Cycles)

[<Fact>]
let ``LD B A copies accumulator`` () =
    let bus = makeBus [| 0x47uy |]
    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with A = 0x99uy; B = 0x00uy } }

    let result = Cpu.step cpu bus

    Assert.Equal(0x99uy, result.Cpu.Registers.B)
    Assert.Equal(0x0101us, result.Cpu.Registers.PC)
    Assert.Equal(4, result.Cycles)

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
let ``CB 87 resets bit zero of A`` () =
    let bus = makeBus [| 0xCBuy; 0x87uy |]
    let cpu = { Cpu.initialState with Registers = { Cpu.initialRegisters with A = 0xFFuy; F = Cpu.ZeroFlag } }

    let result = Cpu.step cpu bus

    Assert.Equal(0xFEuy, result.Cpu.Registers.A)
    Assert.Equal(Cpu.ZeroFlag, result.Cpu.Registers.F)
    Assert.Equal(0x0102us, result.Cpu.Registers.PC)
    Assert.Equal(8, result.Cycles)
