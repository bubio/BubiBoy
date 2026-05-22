namespace BubiBoy.Core

module Cpu =
    exception UnsupportedOpcode of opcode: byte * pc: uint16

    type Registers =
        { A: byte
          F: byte
          B: byte
          C: byte
          D: byte
          E: byte
          H: byte
          L: byte
          SP: uint16
          PC: uint16 }

    type State =
        { Registers: Registers
          Halted: bool }

    type StepResult =
        { Cpu: State
          Bus: Bus.Memory
          Cycles: int }

    [<Literal>]
    let ZeroFlag = 0x80uy

    [<Literal>]
    let SubtractFlag = 0x40uy

    [<Literal>]
    let HalfCarryFlag = 0x20uy

    [<Literal>]
    let CarryFlag = 0x10uy

    let initialRegisters =
        { A = 0x01uy
          F = 0xB0uy
          B = 0x00uy
          C = 0x13uy
          D = 0x00uy
          E = 0xD8uy
          H = 0x01uy
          L = 0x4Duy
          SP = 0xFFFEus
          PC = 0x0100us }

    let initialState =
        { Registers = initialRegisters
          Halted = false }

    let private combineBytes high low =
        (uint16 high <<< 8) ||| uint16 low

    let private readImmediate16 bus pc =
        let low = Bus.readByte pc bus
        let high = Bus.readByte (pc + 1us) bus
        combineBytes high low

    let private write16ToStack value sp bus =
        let high, low = byte (value >>> 8), byte (value &&& 0x00FFus)
        let spAfterHigh = sp - 1us
        let bus = Bus.writeByte spAfterHigh high bus
        let spAfterLow = spAfterHigh - 1us
        let bus = Bus.writeByte spAfterLow low bus
        spAfterLow, bus

    let private split16 value =
        byte (value >>> 8), byte (value &&& 0x00FFus)

    let private getHL registers =
        combineBytes registers.H registers.L

    let private setHL value registers =
        let high, low = split16 value
        { registers with H = high; L = low }

    let private setFlags zero subtract halfCarry carry registers =
        let flag condition value =
            if condition then value else 0uy

        { registers with
            F =
                (flag zero ZeroFlag)
                ||| (flag subtract SubtractFlag)
                ||| (flag halfCarry HalfCarryFlag)
                ||| (flag carry CarryFlag) }

    let private preserveCarry registers =
        registers.F &&& CarryFlag <> 0uy

    let private dec8 value registers =
        let result = value - 1uy
        let halfCarry = value &&& 0x0Fuy = 0uy
        result, setFlags (result = 0uy) true halfCarry (preserveCarry registers) registers

    let private compareA value registers =
        let a = registers.A
        let result = a - value
        let halfCarry = (a &&& 0x0Fuy) < (value &&& 0x0Fuy)
        let carry = a < value
        setFlags (result = 0uy) true halfCarry carry registers

    let private andA value registers =
        let result = registers.A &&& value

        { registers with A = result }
        |> setFlags (result = 0uy) false true false

    let private jumpRelative pc offset =
        let signedOffset =
            if offset < 0x80uy then
                int offset
            else
                int offset - 0x100

        uint16 (int pc + 2 + signedOffset)

    let step cpu bus =
        if cpu.Halted then
            { Cpu = cpu
              Bus = bus
              Cycles = 4 }
        else
            let registers = cpu.Registers
            let opcode = Bus.readByte registers.PC bus

            match opcode with
            | 0x00uy ->
                { Cpu = { cpu with Registers = { registers with PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 4 }
            | 0x05uy ->
                let result, nextRegisters = dec8 registers.B registers

                { Cpu = { cpu with Registers = { nextRegisters with B = result; PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 4 }
            | 0x06uy ->
                let value = Bus.readByte (registers.PC + 1us) bus

                { Cpu = { cpu with Registers = { registers with B = value; PC = registers.PC + 2us } }
                  Bus = bus
                  Cycles = 8 }
            | 0x0Duy ->
                let result, nextRegisters = dec8 registers.C registers

                { Cpu = { cpu with Registers = { nextRegisters with C = result; PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 4 }
            | 0x0Euy ->
                let value = Bus.readByte (registers.PC + 1us) bus

                { Cpu = { cpu with Registers = { registers with C = value; PC = registers.PC + 2us } }
                  Bus = bus
                  Cycles = 8 }
            | 0x16uy ->
                let value = Bus.readByte (registers.PC + 1us) bus

                { Cpu = { cpu with Registers = { registers with D = value; PC = registers.PC + 2us } }
                  Bus = bus
                  Cycles = 8 }
            | 0x18uy ->
                let offset = Bus.readByte (registers.PC + 1us) bus

                { Cpu = { cpu with Registers = { registers with PC = jumpRelative registers.PC offset } }
                  Bus = bus
                  Cycles = 12 }
            | 0x1Euy ->
                let value = Bus.readByte (registers.PC + 1us) bus

                { Cpu = { cpu with Registers = { registers with E = value; PC = registers.PC + 2us } }
                  Bus = bus
                  Cycles = 8 }
            | 0x20uy ->
                let offset = Bus.readByte (registers.PC + 1us) bus

                if registers.F &&& ZeroFlag = 0uy then
                    { Cpu = { cpu with Registers = { registers with PC = jumpRelative registers.PC offset } }
                      Bus = bus
                      Cycles = 12 }
                else
                    { Cpu = { cpu with Registers = { registers with PC = registers.PC + 2us } }
                      Bus = bus
                      Cycles = 8 }
            | 0x38uy ->
                let offset = Bus.readByte (registers.PC + 1us) bus

                if registers.F &&& CarryFlag <> 0uy then
                    { Cpu = { cpu with Registers = { registers with PC = jumpRelative registers.PC offset } }
                      Bus = bus
                      Cycles = 12 }
                else
                    { Cpu = { cpu with Registers = { registers with PC = registers.PC + 2us } }
                      Bus = bus
                      Cycles = 8 }
            | 0x21uy ->
                let value = readImmediate16 bus (registers.PC + 1us)

                { Cpu = { cpu with Registers = registers |> setHL value |> fun next -> { next with PC = registers.PC + 3us } }
                  Bus = bus
                  Cycles = 12 }
            | 0x26uy ->
                let value = Bus.readByte (registers.PC + 1us) bus

                { Cpu = { cpu with Registers = { registers with H = value; PC = registers.PC + 2us } }
                  Bus = bus
                  Cycles = 8 }
            | 0x2Euy ->
                let value = Bus.readByte (registers.PC + 1us) bus

                { Cpu = { cpu with Registers = { registers with L = value; PC = registers.PC + 2us } }
                  Bus = bus
                  Cycles = 8 }
            | 0x31uy ->
                let value = readImmediate16 bus (registers.PC + 1us)

                { Cpu = { cpu with Registers = { registers with SP = value; PC = registers.PC + 3us } }
                  Bus = bus
                  Cycles = 12 }
            | 0x32uy ->
                let address = getHL registers
                let bus = Bus.writeByte address registers.A bus
                let nextRegisters = registers |> setHL (address - 1us)

                { Cpu = { cpu with Registers = { nextRegisters with PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 8 }
            | 0x3Euy ->
                let value = Bus.readByte (registers.PC + 1us) bus

                { Cpu = { cpu with Registers = { registers with A = value; PC = registers.PC + 2us } }
                  Bus = bus
                  Cycles = 8 }
            | 0x47uy ->
                { Cpu = { cpu with Registers = { registers with B = registers.A; PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 4 }
            | 0x7Cuy ->
                { Cpu = { cpu with Registers = { registers with A = registers.H; PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 4 }
            | 0xAFuy ->
                let nextRegisters =
                    { registers with
                        A = 0uy
                        PC = registers.PC + 1us }
                    |> setFlags true false false false

                { Cpu = { cpu with Registers = nextRegisters }
                  Bus = bus
                  Cycles = 4 }
            | 0xC3uy ->
                let target = readImmediate16 bus (registers.PC + 1us)

                { Cpu = { cpu with Registers = { registers with PC = target } }
                  Bus = bus
                  Cycles = 16 }
            | 0xCDuy ->
                let target = readImmediate16 bus (registers.PC + 1us)
                let returnAddress = registers.PC + 3us
                let sp, bus = write16ToStack returnAddress registers.SP bus

                { Cpu = { cpu with Registers = { registers with SP = sp; PC = target } }
                  Bus = bus
                  Cycles = 24 }
            | 0xCBuy ->
                let prefixed = Bus.readByte (registers.PC + 1us) bus

                match prefixed with
                | 0x87uy ->
                    { Cpu = { cpu with Registers = { registers with A = registers.A &&& 0xFEuy; PC = registers.PC + 2us } }
                      Bus = bus
                      Cycles = 8 }
                | unsupported ->
                    raise (UnsupportedOpcode(unsupported, registers.PC + 1us))
            | 0xBEuy ->
                let value = Bus.readByte (getHL registers) bus
                let nextRegisters = compareA value registers

                { Cpu = { cpu with Registers = { nextRegisters with PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 8 }
            | 0xE6uy ->
                let value = Bus.readByte (registers.PC + 1us) bus
                let nextRegisters = andA value registers

                { Cpu = { cpu with Registers = { nextRegisters with PC = registers.PC + 2us } }
                  Bus = bus
                  Cycles = 8 }
            | 0xE0uy ->
                let offset = Bus.readByte (registers.PC + 1us) bus
                let address = 0xFF00us + uint16 offset
                let bus = Bus.writeByte address registers.A bus

                { Cpu = { cpu with Registers = { registers with PC = registers.PC + 2us } }
                  Bus = bus
                  Cycles = 12 }
            | 0xEAuy ->
                let address = readImmediate16 bus (registers.PC + 1us)
                let bus = Bus.writeByte address registers.A bus

                { Cpu = { cpu with Registers = { registers with PC = registers.PC + 3us } }
                  Bus = bus
                  Cycles = 16 }
            | 0xF3uy ->
                { Cpu = { cpu with Registers = { registers with PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 4 }
            | 0xF0uy ->
                let offset = Bus.readByte (registers.PC + 1us) bus
                let address = 0xFF00us + uint16 offset
                let value = Bus.readByte address bus

                { Cpu = { cpu with Registers = { registers with A = value; PC = registers.PC + 2us } }
                  Bus = bus
                  Cycles = 12 }
            | 0xFEuy ->
                let value = Bus.readByte (registers.PC + 1us) bus
                let nextRegisters = compareA value registers

                { Cpu = { cpu with Registers = { nextRegisters with PC = registers.PC + 2us } }
                  Bus = bus
                  Cycles = 8 }
            | unsupported ->
                raise (UnsupportedOpcode(unsupported, registers.PC))
