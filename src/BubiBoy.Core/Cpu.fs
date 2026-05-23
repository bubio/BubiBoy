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
          Halted: bool
          InterruptsEnabled: bool }

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
          Halted = false
          InterruptsEnabled = false }

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

    let private read16FromStack sp bus =
        let low = Bus.readByte sp bus
        let high = Bus.readByte (sp + 1us) bus
        combineBytes high low, sp + 2us

    let private pendingInterrupt bus =
        let enabled = Bus.readByte 0xFFFFus bus
        let flags = Bus.readByte 0xFF0Fus bus
        let pending = enabled &&& flags

        if pending &&& Interrupt.VBlankBit <> 0uy then
            Some(Interrupt.VBlankBit, 0x0040us)
        elif pending &&& Interrupt.LcdStatBit <> 0uy then
            Some(Interrupt.LcdStatBit, 0x0048us)
        elif pending &&& Interrupt.TimerBit <> 0uy then
            Some(Interrupt.TimerBit, 0x0050us)
        elif pending &&& Interrupt.SerialBit <> 0uy then
            Some(Interrupt.SerialBit, 0x0058us)
        elif pending &&& Interrupt.JoypadBit <> 0uy then
            Some(Interrupt.JoypadBit, 0x0060us)
        else
            None

    let private serviceInterrupt flag vector cpu bus =
        let registers = cpu.Registers
        let flags = Bus.readByte 0xFF0Fus bus &&& ~~~flag
        let bus = Bus.writeByte 0xFF0Fus flags bus
        let sp, bus = write16ToStack registers.PC registers.SP bus

        { Cpu =
            { cpu with
                Registers = { registers with SP = sp; PC = vector }
                Halted = false
                InterruptsEnabled = false }
          Bus = bus
          Cycles = 20 }

    let private split16 value =
        byte (value >>> 8), byte (value &&& 0x00FFus)

    let private getHL registers =
        combineBytes registers.H registers.L

    let private getBC registers =
        combineBytes registers.B registers.C

    let private getDE registers =
        combineBytes registers.D registers.E

    let private setBC value registers =
        let high, low = split16 value
        { registers with B = high; C = low }

    let private setDE value registers =
        let high, low = split16 value
        { registers with D = high; E = low }

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

    let private inc8 value registers =
        let result = value + 1uy
        let halfCarry = value &&& 0x0Fuy = 0x0Fuy
        result, setFlags (result = 0uy) false halfCarry (preserveCarry registers) registers

    let private compareA value registers =
        let a = registers.A
        let result = a - value
        let halfCarry = (a &&& 0x0Fuy) < (value &&& 0x0Fuy)
        let carry = a < value
        setFlags (result = 0uy) true halfCarry carry registers

    let private addA value registers =
        let a = registers.A
        let sum = uint16 a + uint16 value
        let result = byte (sum &&& 0x00FFus)
        let halfCarry = (a &&& 0x0Fuy) + (value &&& 0x0Fuy) > 0x0Fuy
        let carry = sum > 0x00FFus

        { registers with A = result }
        |> setFlags (result = 0uy) false halfCarry carry

    let private adcA value registers =
        let carryIn = if registers.F &&& CarryFlag <> 0uy then 1uy else 0uy
        let a = registers.A
        let sum = uint16 a + uint16 value + uint16 carryIn
        let result = byte (sum &&& 0x00FFus)
        let halfCarry = uint16 (a &&& 0x0Fuy) + uint16 (value &&& 0x0Fuy) + uint16 carryIn > 0x0Fus
        let carry = sum > 0x00FFus

        { registers with A = result }
        |> setFlags (result = 0uy) false halfCarry carry

    let private subA value registers =
        let a = registers.A
        let result = a - value
        let halfCarry = (a &&& 0x0Fuy) < (value &&& 0x0Fuy)
        let carry = a < value

        { registers with A = result }
        |> setFlags (result = 0uy) true halfCarry carry

    let private sbcA value registers =
        let carryIn = if registers.F &&& CarryFlag <> 0uy then 1uy else 0uy
        let a = registers.A
        let subtrahend = uint16 value + uint16 carryIn
        let result = byte ((uint16 a - subtrahend) &&& 0x00FFus)
        let halfCarry = uint16 (a &&& 0x0Fuy) < uint16 (value &&& 0x0Fuy) + uint16 carryIn
        let carry = uint16 a < subtrahend

        { registers with A = result }
        |> setFlags (result = 0uy) true halfCarry carry

    let private andA value registers =
        let result = registers.A &&& value

        { registers with A = result }
        |> setFlags (result = 0uy) false true false

    let private orA value registers =
        let result = registers.A ||| value

        { registers with A = result }
        |> setFlags (result = 0uy) false false false

    let private xorA value registers =
        let result = registers.A ^^^ value

        { registers with A = result }
        |> setFlags (result = 0uy) false false false

    let private addHL value registers =
        let hl = getHL registers
        let sum = uint32 hl + uint32 value
        let result = uint16 (sum &&& 0xFFFFu)
        let halfCarry = (hl &&& 0x0FFFus) + (value &&& 0x0FFFus) > 0x0FFFus
        let carry = sum > 0xFFFFu

        registers
        |> setHL result
        |> setFlags (registers.F &&& ZeroFlag <> 0uy) false halfCarry carry

    let private srl8 value registers =
        let carry = value &&& 0x01uy <> 0uy
        let result = value >>> 1
        result, setFlags (result = 0uy) false false carry registers

    let private sra8 value registers =
        let carry = value &&& 0x01uy <> 0uy
        let result = (value >>> 1) ||| (value &&& 0x80uy)
        result, setFlags (result = 0uy) false false carry registers

    let private sla8 value registers =
        let carry = value &&& 0x80uy <> 0uy
        let result = value <<< 1
        result, setFlags (result = 0uy) false false carry registers

    let private rr8 value registers =
        let carryIn = if registers.F &&& CarryFlag <> 0uy then 0x80uy else 0uy
        let carry = value &&& 0x01uy <> 0uy
        let result = (value >>> 1) ||| carryIn
        result, setFlags (result = 0uy) false false carry registers

    let private rrc8 value registers =
        let carry = value &&& 0x01uy <> 0uy
        let result = (value >>> 1) ||| if carry then 0x80uy else 0uy
        result, setFlags (result = 0uy) false false carry registers

    let private rl8 value registers =
        let carryIn = if registers.F &&& CarryFlag <> 0uy then 0x01uy else 0uy
        let carry = value &&& 0x80uy <> 0uy
        let result = (value <<< 1) ||| carryIn
        result, setFlags (result = 0uy) false false carry registers

    let private swap8 value registers =
        let result = (value >>> 4) ||| (value <<< 4)
        result, setFlags (result = 0uy) false false false registers

    let private bitTest bit value registers =
        let mask = 1uy <<< bit
        registers
        |> setFlags (value &&& mask = 0uy) false true (registers.F &&& CarryFlag <> 0uy)

    let private decimalAdjust registers =
        let subtract = registers.F &&& SubtractFlag <> 0uy
        let halfCarry = registers.F &&& HalfCarryFlag <> 0uy
        let carry = registers.F &&& CarryFlag <> 0uy
        let mutable correction = 0
        let mutable setCarry = carry

        if halfCarry || (not subtract && (registers.A &&& 0x0Fuy) > 0x09uy) then
            correction <- correction ||| 0x06

        if carry || (not subtract && registers.A > 0x99uy) then
            correction <- correction ||| 0x60
            setCarry <- true

        let adjusted =
            if subtract then
                byte (int registers.A - correction)
            else
                byte (int registers.A + correction)

        { registers with A = adjusted }
        |> setFlags (adjusted = 0uy) subtract false setCarry

    let private jumpRelative pc offset =
        let signedOffset =
            if offset < 0x80uy then
                int offset
            else
                int offset - 0x100

        uint16 (int pc + 2 + signedOffset)

    let step cpu bus =
        match pendingInterrupt bus with
        | Some(flag, vector) when cpu.InterruptsEnabled ->
            serviceInterrupt flag vector cpu bus
        | _ when cpu.Halted ->
            { Cpu = cpu
              Bus = bus
              Cycles = 4 }
        | _ ->
            let registers = cpu.Registers
            let opcode = Bus.readByte registers.PC bus

            match opcode with
            | 0x00uy ->
                { Cpu = { cpu with Registers = { registers with PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 4 }
            | 0x01uy ->
                let value = readImmediate16 bus (registers.PC + 1us)

                { Cpu = { cpu with Registers = registers |> setBC value |> fun next -> { next with PC = registers.PC + 3us } }
                  Bus = bus
                  Cycles = 12 }
            | 0x02uy ->
                let bus = Bus.writeByte (getBC registers) registers.A bus

                { Cpu = { cpu with Registers = { registers with PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 8 }
            | 0x03uy ->
                let nextRegisters = registers |> setBC (getBC registers + 1us)

                { Cpu = { cpu with Registers = { nextRegisters with PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 8 }
            | 0x04uy ->
                let result, nextRegisters = inc8 registers.B registers

                { Cpu = { cpu with Registers = { nextRegisters with B = result; PC = registers.PC + 1us } }
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
            | 0x07uy ->
                let carry = registers.A &&& 0x80uy <> 0uy
                let value = (registers.A <<< 1) ||| if carry then 0x01uy else 0uy
                let nextRegisters =
                    { registers with A = value }
                    |> setFlags false false false carry

                { Cpu = { cpu with Registers = { nextRegisters with PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 4 }
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
            | 0x0Fuy ->
                let carry = registers.A &&& 0x01uy <> 0uy
                let value = (registers.A >>> 1) ||| if carry then 0x80uy else 0uy
                let nextRegisters =
                    { registers with A = value }
                    |> setFlags false false false carry

                { Cpu = { cpu with Registers = { nextRegisters with PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 4 }
            | 0x0Buy ->
                let nextRegisters = registers |> setBC (getBC registers - 1us)

                { Cpu = { cpu with Registers = { nextRegisters with PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 8 }
            | 0x0Cuy ->
                let result, nextRegisters = inc8 registers.C registers

                { Cpu = { cpu with Registers = { nextRegisters with C = result; PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 4 }
            | 0x0Auy ->
                let value = Bus.readByte (getBC registers) bus

                { Cpu = { cpu with Registers = { registers with A = value; PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 8 }
            | 0x09uy ->
                let nextRegisters = addHL (getBC registers) registers

                { Cpu = { cpu with Registers = { nextRegisters with PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 8 }
            | 0x11uy ->
                let value = readImmediate16 bus (registers.PC + 1us)

                { Cpu = { cpu with Registers = registers |> setDE value |> fun next -> { next with PC = registers.PC + 3us } }
                  Bus = bus
                  Cycles = 12 }
            | 0x12uy ->
                let bus = Bus.writeByte (getDE registers) registers.A bus

                { Cpu = { cpu with Registers = { registers with PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 8 }
            | 0x13uy ->
                let nextRegisters = registers |> setDE (getDE registers + 1us)

                { Cpu = { cpu with Registers = { nextRegisters with PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 8 }
            | 0x14uy ->
                let result, nextRegisters = inc8 registers.D registers

                { Cpu = { cpu with Registers = { nextRegisters with D = result; PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 4 }
            | 0x16uy ->
                let value = Bus.readByte (registers.PC + 1us) bus

                { Cpu = { cpu with Registers = { registers with D = value; PC = registers.PC + 2us } }
                  Bus = bus
                  Cycles = 8 }
            | 0x15uy ->
                let result, nextRegisters = dec8 registers.D registers

                { Cpu = { cpu with Registers = { nextRegisters with D = result; PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 4 }
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
            | 0x1Fuy ->
                let carryIn = if registers.F &&& CarryFlag <> 0uy then 0x80uy else 0uy
                let carry = registers.A &&& 0x01uy <> 0uy
                let value = (registers.A >>> 1) ||| carryIn
                let nextRegisters =
                    { registers with A = value }
                    |> setFlags false false false carry

                { Cpu = { cpu with Registers = { nextRegisters with PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 4 }
            | 0x1Duy ->
                let result, nextRegisters = dec8 registers.E registers

                { Cpu = { cpu with Registers = { nextRegisters with E = result; PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 4 }
            | 0x1Cuy ->
                let result, nextRegisters = inc8 registers.E registers

                { Cpu = { cpu with Registers = { nextRegisters with E = result; PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 4 }
            | 0x1Auy ->
                let value = Bus.readByte (getDE registers) bus

                { Cpu = { cpu with Registers = { registers with A = value; PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 8 }
            | 0x19uy ->
                let nextRegisters = addHL (getDE registers) registers

                { Cpu = { cpu with Registers = { nextRegisters with PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 8 }
            | 0x1Buy ->
                let nextRegisters = registers |> setDE (getDE registers - 1us)

                { Cpu = { cpu with Registers = { nextRegisters with PC = registers.PC + 1us } }
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
            | 0x28uy ->
                let offset = Bus.readByte (registers.PC + 1us) bus

                if registers.F &&& ZeroFlag <> 0uy then
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
            | 0x22uy ->
                let address = getHL registers
                let bus = Bus.writeByte address registers.A bus
                let nextRegisters = registers |> setHL (address + 1us)

                { Cpu = { cpu with Registers = { nextRegisters with PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 8 }
            | 0x23uy ->
                let nextRegisters = registers |> setHL (getHL registers + 1us)

                { Cpu = { cpu with Registers = { nextRegisters with PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 8 }
            | 0x26uy ->
                let value = Bus.readByte (registers.PC + 1us) bus

                { Cpu = { cpu with Registers = { registers with H = value; PC = registers.PC + 2us } }
                  Bus = bus
                  Cycles = 8 }
            | 0x24uy ->
                let result, nextRegisters = inc8 registers.H registers

                { Cpu = { cpu with Registers = { nextRegisters with H = result; PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 4 }
            | 0x2Auy ->
                let address = getHL registers
                let value = Bus.readByte address bus
                let nextRegisters = registers |> setHL (address + 1us)

                { Cpu = { cpu with Registers = { nextRegisters with A = value; PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 8 }
            | 0x2Euy ->
                let value = Bus.readByte (registers.PC + 1us) bus

                { Cpu = { cpu with Registers = { registers with L = value; PC = registers.PC + 2us } }
                  Bus = bus
                  Cycles = 8 }
            | 0x2Cuy ->
                let result, nextRegisters = inc8 registers.L registers

                { Cpu = { cpu with Registers = { nextRegisters with L = result; PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 4 }
            | 0x2Duy ->
                let result, nextRegisters = dec8 registers.L registers

                { Cpu = { cpu with Registers = { nextRegisters with L = result; PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 4 }
            | 0x2Fuy ->
                let nextRegisters =
                    { registers with A = ~~~registers.A }
                    |> setFlags (registers.F &&& ZeroFlag <> 0uy) true true (registers.F &&& CarryFlag <> 0uy)

                { Cpu = { cpu with Registers = { nextRegisters with PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 4 }
            | 0x27uy ->
                let nextRegisters = decimalAdjust registers

                { Cpu = { cpu with Registers = { nextRegisters with PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 4 }
            | 0x29uy ->
                let nextRegisters = addHL (getHL registers) registers

                { Cpu = { cpu with Registers = { nextRegisters with PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 8 }
            | 0x30uy ->
                let offset = Bus.readByte (registers.PC + 1us) bus

                if registers.F &&& CarryFlag = 0uy then
                    { Cpu = { cpu with Registers = { registers with PC = jumpRelative registers.PC offset } }
                      Bus = bus
                      Cycles = 12 }
                else
                    { Cpu = { cpu with Registers = { registers with PC = registers.PC + 2us } }
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
            | 0x3Auy ->
                let address = getHL registers
                let value = Bus.readByte address bus
                let nextRegisters = registers |> setHL (address - 1us)

                { Cpu = { cpu with Registers = { nextRegisters with A = value; PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 8 }
            | 0x34uy ->
                let address = getHL registers
                let value = Bus.readByte address bus
                let result, nextRegisters = inc8 value registers
                let bus = Bus.writeByte address result bus

                { Cpu = { cpu with Registers = { nextRegisters with PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 12 }
            | 0x36uy ->
                let value = Bus.readByte (registers.PC + 1us) bus
                let bus = Bus.writeByte (getHL registers) value bus

                { Cpu = { cpu with Registers = { registers with PC = registers.PC + 2us } }
                  Bus = bus
                  Cycles = 12 }
            | 0x35uy ->
                let address = getHL registers
                let value = Bus.readByte address bus
                let result, nextRegisters = dec8 value registers
                let bus = Bus.writeByte address result bus

                { Cpu = { cpu with Registers = { nextRegisters with PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 12 }
            | 0x37uy ->
                let nextRegisters =
                    registers
                    |> setFlags (registers.F &&& ZeroFlag <> 0uy) false false true

                { Cpu = { cpu with Registers = { nextRegisters with PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 4 }
            | 0x3Fuy ->
                let nextRegisters =
                    registers
                    |> setFlags (registers.F &&& ZeroFlag <> 0uy) false false (registers.F &&& CarryFlag = 0uy)

                { Cpu = { cpu with Registers = { nextRegisters with PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 4 }
            | 0x39uy ->
                let nextRegisters = addHL registers.SP registers

                { Cpu = { cpu with Registers = { nextRegisters with PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 8 }
            | 0x3Euy ->
                let value = Bus.readByte (registers.PC + 1us) bus

                { Cpu = { cpu with Registers = { registers with A = value; PC = registers.PC + 2us } }
                  Bus = bus
                  Cycles = 8 }
            | 0x3Duy ->
                let result, nextRegisters = dec8 registers.A registers

                { Cpu = { cpu with Registers = { nextRegisters with A = result; PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 4 }
            | 0x3Cuy ->
                let result, nextRegisters = inc8 registers.A registers

                { Cpu = { cpu with Registers = { nextRegisters with A = result; PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 4 }
            | 0x40uy ->
                { Cpu = { cpu with Registers = { registers with B = registers.B; PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 4 }
            | 0x41uy ->
                { Cpu = { cpu with Registers = { registers with B = registers.C; PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 4 }
            | 0x42uy ->
                { Cpu = { cpu with Registers = { registers with B = registers.D; PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 4 }
            | 0x43uy ->
                { Cpu = { cpu with Registers = { registers with B = registers.E; PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 4 }
            | 0x44uy ->
                { Cpu = { cpu with Registers = { registers with B = registers.H; PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 4 }
            | 0x45uy ->
                { Cpu = { cpu with Registers = { registers with B = registers.L; PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 4 }
            | 0x46uy ->
                let value = Bus.readByte (getHL registers) bus

                { Cpu = { cpu with Registers = { registers with B = value; PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 8 }
            | 0x47uy ->
                { Cpu = { cpu with Registers = { registers with B = registers.A; PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 4 }
            | 0x4Auy ->
                { Cpu = { cpu with Registers = { registers with C = registers.D; PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 4 }
            | 0x4Fuy ->
                { Cpu = { cpu with Registers = { registers with C = registers.A; PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 4 }
            | 0x4Euy ->
                let value = Bus.readByte (getHL registers) bus

                { Cpu = { cpu with Registers = { registers with C = value; PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 8 }
            | 0x4Duy ->
                { Cpu = { cpu with Registers = { registers with C = registers.L; PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 4 }
            | 0x50uy ->
                { Cpu = { cpu with Registers = { registers with D = registers.B; PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 4 }
            | 0x51uy ->
                { Cpu = { cpu with Registers = { registers with D = registers.C; PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 4 }
            | 0x52uy ->
                { Cpu = { cpu with Registers = { registers with D = registers.D; PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 4 }
            | 0x53uy ->
                { Cpu = { cpu with Registers = { registers with D = registers.E; PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 4 }
            | 0x54uy ->
                { Cpu = { cpu with Registers = { registers with D = registers.H; PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 4 }
            | 0x55uy ->
                { Cpu = { cpu with Registers = { registers with D = registers.L; PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 4 }
            | 0x57uy ->
                { Cpu = { cpu with Registers = { registers with D = registers.A; PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 4 }
            | 0x56uy ->
                let value = Bus.readByte (getHL registers) bus

                { Cpu = { cpu with Registers = { registers with D = value; PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 8 }
            | 0x58uy ->
                { Cpu = { cpu with Registers = { registers with E = registers.B; PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 4 }
            | 0x59uy ->
                { Cpu = { cpu with Registers = { registers with E = registers.C; PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 4 }
            | 0x5Auy ->
                { Cpu = { cpu with Registers = { registers with E = registers.D; PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 4 }
            | 0x5Buy ->
                { Cpu = { cpu with Registers = { registers with E = registers.E; PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 4 }
            | 0x5Cuy ->
                { Cpu = { cpu with Registers = { registers with E = registers.H; PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 4 }
            | 0x5Duy ->
                { Cpu = { cpu with Registers = { registers with E = registers.L; PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 4 }
            | 0x5Euy ->
                let value = Bus.readByte (getHL registers) bus

                { Cpu = { cpu with Registers = { registers with E = value; PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 8 }
            | 0x5Fuy ->
                { Cpu = { cpu with Registers = { registers with E = registers.A; PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 4 }
            | 0x60uy ->
                { Cpu = { cpu with Registers = { registers with H = registers.B; PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 4 }
            | 0x61uy ->
                { Cpu = { cpu with Registers = { registers with H = registers.C; PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 4 }
            | 0x62uy ->
                { Cpu = { cpu with Registers = { registers with H = registers.D; PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 4 }
            | 0x63uy ->
                { Cpu = { cpu with Registers = { registers with H = registers.E; PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 4 }
            | 0x64uy ->
                { Cpu = { cpu with Registers = { registers with H = registers.H; PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 4 }
            | 0x65uy ->
                { Cpu = { cpu with Registers = { registers with H = registers.L; PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 4 }
            | 0x66uy ->
                let value = Bus.readByte (getHL registers) bus

                { Cpu = { cpu with Registers = { registers with H = value; PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 8 }
            | 0x67uy ->
                { Cpu = { cpu with Registers = { registers with H = registers.A; PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 4 }
            | 0x68uy ->
                { Cpu = { cpu with Registers = { registers with L = registers.B; PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 4 }
            | 0x69uy ->
                { Cpu = { cpu with Registers = { registers with L = registers.C; PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 4 }
            | 0x6Auy ->
                { Cpu = { cpu with Registers = { registers with L = registers.D; PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 4 }
            | 0x6Buy ->
                { Cpu = { cpu with Registers = { registers with L = registers.E; PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 4 }
            | 0x6Cuy ->
                { Cpu = { cpu with Registers = { registers with L = registers.H; PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 4 }
            | 0x6Duy ->
                { Cpu = { cpu with Registers = { registers with L = registers.L; PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 4 }
            | 0x6Euy ->
                let value = Bus.readByte (getHL registers) bus

                { Cpu = { cpu with Registers = { registers with L = value; PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 8 }
            | 0x6Fuy ->
                { Cpu = { cpu with Registers = { registers with L = registers.A; PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 4 }
            | 0x70uy ->
                let bus = Bus.writeByte (getHL registers) registers.B bus

                { Cpu = { cpu with Registers = { registers with PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 8 }
            | 0x71uy ->
                let bus = Bus.writeByte (getHL registers) registers.C bus

                { Cpu = { cpu with Registers = { registers with PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 8 }
            | 0x72uy ->
                let bus = Bus.writeByte (getHL registers) registers.D bus

                { Cpu = { cpu with Registers = { registers with PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 8 }
            | 0x73uy ->
                let bus = Bus.writeByte (getHL registers) registers.E bus

                { Cpu = { cpu with Registers = { registers with PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 8 }
            | 0x76uy ->
                { Cpu = { cpu with Halted = true; Registers = { registers with PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 4 }
            | 0x77uy ->
                let bus = Bus.writeByte (getHL registers) registers.A bus

                { Cpu = { cpu with Registers = { registers with PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 8 }
            | 0x7Cuy ->
                { Cpu = { cpu with Registers = { registers with A = registers.H; PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 4 }
            | 0x78uy ->
                { Cpu = { cpu with Registers = { registers with A = registers.B; PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 4 }
            | 0x79uy ->
                { Cpu = { cpu with Registers = { registers with A = registers.C; PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 4 }
            | 0x7Auy ->
                { Cpu = { cpu with Registers = { registers with A = registers.D; PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 4 }
            | 0x7Buy ->
                { Cpu = { cpu with Registers = { registers with A = registers.E; PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 4 }
            | 0x7Duy ->
                { Cpu = { cpu with Registers = { registers with A = registers.L; PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 4 }
            | 0x7Euy ->
                let value = Bus.readByte (getHL registers) bus

                { Cpu = { cpu with Registers = { registers with A = value; PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 8 }
            | 0x7Fuy ->
                { Cpu = { cpu with Registers = { registers with A = registers.A; PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 4 }
            | 0x80uy ->
                let nextRegisters = addA registers.B registers

                { Cpu = { cpu with Registers = { nextRegisters with PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 4 }
            | 0x81uy ->
                let nextRegisters = addA registers.C registers

                { Cpu = { cpu with Registers = { nextRegisters with PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 4 }
            | 0x82uy ->
                let nextRegisters = addA registers.D registers

                { Cpu = { cpu with Registers = { nextRegisters with PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 4 }
            | 0x83uy ->
                let nextRegisters = addA registers.E registers

                { Cpu = { cpu with Registers = { nextRegisters with PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 4 }
            | 0x84uy ->
                let nextRegisters = addA registers.H registers

                { Cpu = { cpu with Registers = { nextRegisters with PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 4 }
            | 0x85uy ->
                let nextRegisters = addA registers.L registers

                { Cpu = { cpu with Registers = { nextRegisters with PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 4 }
            | 0x86uy ->
                let value = Bus.readByte (getHL registers) bus
                let nextRegisters = addA value registers

                { Cpu = { cpu with Registers = { nextRegisters with PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 8 }
            | 0x87uy ->
                let nextRegisters = addA registers.A registers

                { Cpu = { cpu with Registers = { nextRegisters with PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 4 }
            | 0x88uy ->
                let nextRegisters = adcA registers.B registers

                { Cpu = { cpu with Registers = { nextRegisters with PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 4 }
            | 0x8Auy ->
                let nextRegisters = adcA registers.D registers

                { Cpu = { cpu with Registers = { nextRegisters with PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 4 }
            | 0x90uy ->
                let nextRegisters = subA registers.B registers

                { Cpu = { cpu with Registers = { nextRegisters with PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 4 }
            | 0x91uy ->
                let nextRegisters = subA registers.C registers

                { Cpu = { cpu with Registers = { nextRegisters with PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 4 }
            | 0x92uy ->
                let nextRegisters = subA registers.D registers

                { Cpu = { cpu with Registers = { nextRegisters with PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 4 }
            | 0x93uy ->
                let nextRegisters = subA registers.E registers

                { Cpu = { cpu with Registers = { nextRegisters with PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 4 }
            | 0x94uy ->
                let nextRegisters = subA registers.H registers

                { Cpu = { cpu with Registers = { nextRegisters with PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 4 }
            | 0x95uy ->
                let nextRegisters = subA registers.L registers

                { Cpu = { cpu with Registers = { nextRegisters with PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 4 }
            | 0x97uy ->
                let nextRegisters = subA registers.A registers

                { Cpu = { cpu with Registers = { nextRegisters with PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 4 }
            | 0x96uy ->
                let value = Bus.readByte (getHL registers) bus
                let nextRegisters = subA value registers

                { Cpu = { cpu with Registers = { nextRegisters with PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 8 }
            | 0x98uy ->
                let nextRegisters = sbcA registers.B registers

                { Cpu = { cpu with Registers = { nextRegisters with PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 4 }
            | 0x99uy ->
                let nextRegisters = sbcA registers.C registers

                { Cpu = { cpu with Registers = { nextRegisters with PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 4 }
            | 0x9Auy ->
                let nextRegisters = sbcA registers.D registers

                { Cpu = { cpu with Registers = { nextRegisters with PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 4 }
            | 0x9Buy ->
                let nextRegisters = sbcA registers.E registers

                { Cpu = { cpu with Registers = { nextRegisters with PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 4 }
            | 0x9Cuy ->
                let nextRegisters = sbcA registers.H registers

                { Cpu = { cpu with Registers = { nextRegisters with PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 4 }
            | 0x9Duy ->
                let nextRegisters = sbcA registers.L registers

                { Cpu = { cpu with Registers = { nextRegisters with PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 4 }
            | 0x9Fuy ->
                let nextRegisters = sbcA registers.A registers

                { Cpu = { cpu with Registers = { nextRegisters with PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 4 }
            | 0x9Euy ->
                let value = Bus.readByte (getHL registers) bus
                let nextRegisters = sbcA value registers

                { Cpu = { cpu with Registers = { nextRegisters with PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 8 }
            | 0xAFuy ->
                let nextRegisters = xorA registers.A registers

                { Cpu = { cpu with Registers = { nextRegisters with PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 4 }
            | 0xA8uy ->
                let nextRegisters = xorA registers.B registers

                { Cpu = { cpu with Registers = { nextRegisters with PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 4 }
            | 0xA9uy ->
                let nextRegisters = xorA registers.C registers

                { Cpu = { cpu with Registers = { nextRegisters with PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 4 }
            | 0xAEuy ->
                let value = Bus.readByte (getHL registers) bus
                let nextRegisters = xorA value registers

                { Cpu = { cpu with Registers = { nextRegisters with PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 8 }
            | 0xA1uy ->
                let nextRegisters = andA registers.C registers

                { Cpu = { cpu with Registers = { nextRegisters with PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 4 }
            | 0xA0uy ->
                let nextRegisters = andA registers.B registers

                { Cpu = { cpu with Registers = { nextRegisters with PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 4 }
            | 0xA3uy ->
                let nextRegisters = andA registers.E registers

                { Cpu = { cpu with Registers = { nextRegisters with PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 4 }
            | 0xA6uy ->
                let value = Bus.readByte (getHL registers) bus
                let nextRegisters = andA value registers

                { Cpu = { cpu with Registers = { nextRegisters with PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 8 }
            | 0xA7uy ->
                let nextRegisters = andA registers.A registers

                { Cpu = { cpu with Registers = { nextRegisters with PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 4 }
            | 0xB1uy ->
                let nextRegisters = orA registers.C registers

                { Cpu = { cpu with Registers = { nextRegisters with PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 4 }
            | 0xB2uy ->
                let nextRegisters = orA registers.D registers

                { Cpu = { cpu with Registers = { nextRegisters with PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 4 }
            | 0xB3uy ->
                let nextRegisters = orA registers.E registers

                { Cpu = { cpu with Registers = { nextRegisters with PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 4 }
            | 0xB5uy ->
                let nextRegisters = orA registers.L registers

                { Cpu = { cpu with Registers = { nextRegisters with PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 4 }
            | 0xB6uy ->
                let value = Bus.readByte (getHL registers) bus
                let nextRegisters = orA value registers

                { Cpu = { cpu with Registers = { nextRegisters with PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 8 }
            | 0xB7uy ->
                let nextRegisters = orA registers.A registers

                { Cpu = { cpu with Registers = { nextRegisters with PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 4 }
            | 0xB0uy ->
                let nextRegisters = orA registers.B registers

                { Cpu = { cpu with Registers = { nextRegisters with PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 4 }
            | 0xB8uy ->
                let nextRegisters = compareA registers.B registers

                { Cpu = { cpu with Registers = { nextRegisters with PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 4 }
            | 0xB9uy ->
                let nextRegisters = compareA registers.C registers

                { Cpu = { cpu with Registers = { nextRegisters with PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 4 }
            | 0xBAuy ->
                let nextRegisters = compareA registers.D registers

                { Cpu = { cpu with Registers = { nextRegisters with PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 4 }
            | 0xBBuy ->
                let nextRegisters = compareA registers.E registers

                { Cpu = { cpu with Registers = { nextRegisters with PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 4 }
            | 0xBCuy ->
                let nextRegisters = compareA registers.H registers

                { Cpu = { cpu with Registers = { nextRegisters with PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 4 }
            | 0xBDuy ->
                let nextRegisters = compareA registers.L registers

                { Cpu = { cpu with Registers = { nextRegisters with PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 4 }
            | 0xBFuy ->
                let nextRegisters = compareA registers.A registers

                { Cpu = { cpu with Registers = { nextRegisters with PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 4 }
            | 0xC1uy ->
                let value, sp = read16FromStack registers.SP bus
                let nextRegisters = registers |> setBC value

                { Cpu = { cpu with Registers = { nextRegisters with SP = sp; PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 12 }
            | 0xC0uy ->
                if registers.F &&& ZeroFlag = 0uy then
                    let target, sp = read16FromStack registers.SP bus

                    { Cpu = { cpu with Registers = { registers with SP = sp; PC = target } }
                      Bus = bus
                      Cycles = 20 }
                else
                    { Cpu = { cpu with Registers = { registers with PC = registers.PC + 1us } }
                      Bus = bus
                      Cycles = 8 }
            | 0xC3uy ->
                let target = readImmediate16 bus (registers.PC + 1us)

                { Cpu = { cpu with Registers = { registers with PC = target } }
                  Bus = bus
                  Cycles = 16 }
            | 0xC2uy ->
                let target = readImmediate16 bus (registers.PC + 1us)

                if registers.F &&& ZeroFlag = 0uy then
                    { Cpu = { cpu with Registers = { registers with PC = target } }
                      Bus = bus
                      Cycles = 16 }
                else
                    { Cpu = { cpu with Registers = { registers with PC = registers.PC + 3us } }
                      Bus = bus
                      Cycles = 12 }
            | 0xC4uy ->
                let target = readImmediate16 bus (registers.PC + 1us)

                if registers.F &&& ZeroFlag = 0uy then
                    let returnAddress = registers.PC + 3us
                    let sp, bus = write16ToStack returnAddress registers.SP bus

                    { Cpu = { cpu with Registers = { registers with SP = sp; PC = target } }
                      Bus = bus
                      Cycles = 24 }
                else
                    { Cpu = { cpu with Registers = { registers with PC = registers.PC + 3us } }
                      Bus = bus
                      Cycles = 12 }
            | 0xC5uy ->
                let sp, bus = write16ToStack (getBC registers) registers.SP bus

                { Cpu = { cpu with Registers = { registers with SP = sp; PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 16 }
            | 0xC6uy ->
                let value = Bus.readByte (registers.PC + 1us) bus
                let nextRegisters = addA value registers

                { Cpu = { cpu with Registers = { nextRegisters with PC = registers.PC + 2us } }
                  Bus = bus
                  Cycles = 8 }
            | 0xCEuy ->
                let value = Bus.readByte (registers.PC + 1us) bus
                let nextRegisters = adcA value registers

                { Cpu = { cpu with Registers = { nextRegisters with PC = registers.PC + 2us } }
                  Bus = bus
                  Cycles = 8 }
            | 0xC9uy ->
                let target, sp = read16FromStack registers.SP bus

                { Cpu = { cpu with Registers = { registers with SP = sp; PC = target } }
                  Bus = bus
                  Cycles = 16 }
            | 0xC8uy ->
                if registers.F &&& ZeroFlag <> 0uy then
                    let target, sp = read16FromStack registers.SP bus

                    { Cpu = { cpu with Registers = { registers with SP = sp; PC = target } }
                      Bus = bus
                      Cycles = 20 }
                else
                    { Cpu = { cpu with Registers = { registers with PC = registers.PC + 1us } }
                      Bus = bus
                      Cycles = 8 }
            | 0xCAuy ->
                let target = readImmediate16 bus (registers.PC + 1us)

                if registers.F &&& ZeroFlag <> 0uy then
                    { Cpu = { cpu with Registers = { registers with PC = target } }
                      Bus = bus
                      Cycles = 16 }
                else
                    { Cpu = { cpu with Registers = { registers with PC = registers.PC + 3us } }
                      Bus = bus
                      Cycles = 12 }
            | 0xCCuy ->
                let target = readImmediate16 bus (registers.PC + 1us)

                if registers.F &&& ZeroFlag <> 0uy then
                    let returnAddress = registers.PC + 3us
                    let sp, bus = write16ToStack returnAddress registers.SP bus

                    { Cpu = { cpu with Registers = { registers with SP = sp; PC = target } }
                      Bus = bus
                      Cycles = 24 }
                else
                    { Cpu = { cpu with Registers = { registers with PC = registers.PC + 3us } }
                      Bus = bus
                      Cycles = 12 }
            | 0xD1uy ->
                let value, sp = read16FromStack registers.SP bus
                let nextRegisters = registers |> setDE value

                { Cpu = { cpu with Registers = { nextRegisters with SP = sp; PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 12 }
            | 0xD0uy ->
                if registers.F &&& CarryFlag = 0uy then
                    let target, sp = read16FromStack registers.SP bus

                    { Cpu = { cpu with Registers = { registers with SP = sp; PC = target } }
                      Bus = bus
                      Cycles = 20 }
                else
                    { Cpu = { cpu with Registers = { registers with PC = registers.PC + 1us } }
                      Bus = bus
                      Cycles = 8 }
            | 0xD8uy ->
                if registers.F &&& CarryFlag <> 0uy then
                    let target, sp = read16FromStack registers.SP bus

                    { Cpu = { cpu with Registers = { registers with SP = sp; PC = target } }
                      Bus = bus
                      Cycles = 20 }
                else
                    { Cpu = { cpu with Registers = { registers with PC = registers.PC + 1us } }
                      Bus = bus
                      Cycles = 8 }
            | 0xD5uy ->
                let sp, bus = write16ToStack (getDE registers) registers.SP bus

                { Cpu = { cpu with Registers = { registers with SP = sp; PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 16 }
            | 0xD6uy ->
                let value = Bus.readByte (registers.PC + 1us) bus
                let nextRegisters = subA value registers

                { Cpu = { cpu with Registers = { nextRegisters with PC = registers.PC + 2us } }
                  Bus = bus
                  Cycles = 8 }
            | 0xD9uy ->
                let target, sp = read16FromStack registers.SP bus

                { Cpu =
                    { cpu with
                        Registers = { registers with SP = sp; PC = target }
                        InterruptsEnabled = true }
                  Bus = bus
                  Cycles = 16 }
            | 0xD2uy ->
                let target = readImmediate16 bus (registers.PC + 1us)

                if registers.F &&& CarryFlag = 0uy then
                    { Cpu = { cpu with Registers = { registers with PC = target } }
                      Bus = bus
                      Cycles = 16 }
                else
                    { Cpu = { cpu with Registers = { registers with PC = registers.PC + 3us } }
                      Bus = bus
                      Cycles = 12 }
            | 0xDAuy ->
                let target = readImmediate16 bus (registers.PC + 1us)

                if registers.F &&& CarryFlag <> 0uy then
                    { Cpu = { cpu with Registers = { registers with PC = target } }
                      Bus = bus
                      Cycles = 16 }
                else
                    { Cpu = { cpu with Registers = { registers with PC = registers.PC + 3us } }
                      Bus = bus
                      Cycles = 12 }
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
                | 0x0Euy ->
                    let address = getHL registers
                    let value, nextRegisters = rrc8 (Bus.readByte address bus) registers
                    let bus = Bus.writeByte address value bus

                    { Cpu = { cpu with Registers = { nextRegisters with PC = registers.PC + 2us } }
                      Bus = bus
                      Cycles = 16 }
                | 0x18uy ->
                    let value, nextRegisters = rr8 registers.B registers

                    { Cpu = { cpu with Registers = { nextRegisters with B = value; PC = registers.PC + 2us } }
                      Bus = bus
                      Cycles = 8 }
                | 0x19uy ->
                    let value, nextRegisters = rr8 registers.C registers

                    { Cpu = { cpu with Registers = { nextRegisters with C = value; PC = registers.PC + 2us } }
                      Bus = bus
                      Cycles = 8 }
                | 0x1Auy ->
                    let value, nextRegisters = rr8 registers.D registers

                    { Cpu = { cpu with Registers = { nextRegisters with D = value; PC = registers.PC + 2us } }
                      Bus = bus
                      Cycles = 8 }
                | 0x1Buy ->
                    let value, nextRegisters = rr8 registers.E registers

                    { Cpu = { cpu with Registers = { nextRegisters with E = value; PC = registers.PC + 2us } }
                      Bus = bus
                      Cycles = 8 }
                | 0x1Cuy ->
                    let value, nextRegisters = rr8 registers.H registers

                    { Cpu = { cpu with Registers = { nextRegisters with H = value; PC = registers.PC + 2us } }
                      Bus = bus
                      Cycles = 8 }
                | 0x1Duy ->
                    let value, nextRegisters = rr8 registers.L registers

                    { Cpu = { cpu with Registers = { nextRegisters with L = value; PC = registers.PC + 2us } }
                      Bus = bus
                      Cycles = 8 }
                | 0x1Fuy ->
                    let value, nextRegisters = rr8 registers.A registers

                    { Cpu = { cpu with Registers = { nextRegisters with A = value; PC = registers.PC + 2us } }
                      Bus = bus
                      Cycles = 8 }
                | 0x12uy ->
                    let value, nextRegisters = rl8 registers.D registers

                    { Cpu = { cpu with Registers = { nextRegisters with D = value; PC = registers.PC + 2us } }
                      Bus = bus
                      Cycles = 8 }
                | 0x23uy ->
                    let value, nextRegisters = sla8 registers.E registers

                    { Cpu = { cpu with Registers = { nextRegisters with E = value; PC = registers.PC + 2us } }
                      Bus = bus
                      Cycles = 8 }
                | 0x27uy ->
                    let value, nextRegisters = sla8 registers.A registers

                    { Cpu = { cpu with Registers = { nextRegisters with A = value; PC = registers.PC + 2us } }
                      Bus = bus
                      Cycles = 8 }
                | 0x2Auy ->
                    let value, nextRegisters = sra8 registers.D registers

                    { Cpu = { cpu with Registers = { nextRegisters with D = value; PC = registers.PC + 2us } }
                      Bus = bus
                      Cycles = 8 }
                | 0x33uy ->
                    let value, nextRegisters = swap8 registers.E registers

                    { Cpu = { cpu with Registers = { nextRegisters with E = value; PC = registers.PC + 2us } }
                      Bus = bus
                      Cycles = 8 }
                | 0x36uy ->
                    let address = getHL registers
                    let value, nextRegisters = swap8 (Bus.readByte address bus) registers
                    let bus = Bus.writeByte address value bus

                    { Cpu = { cpu with Registers = { nextRegisters with PC = registers.PC + 2us } }
                      Bus = bus
                      Cycles = 16 }
                | 0x37uy ->
                    let value, nextRegisters = swap8 registers.A registers

                    { Cpu = { cpu with Registers = { nextRegisters with A = value; PC = registers.PC + 2us } }
                      Bus = bus
                      Cycles = 8 }
                | 0x38uy ->
                    let value, nextRegisters = srl8 registers.B registers

                    { Cpu = { cpu with Registers = { nextRegisters with B = value; PC = registers.PC + 2us } }
                      Bus = bus
                      Cycles = 8 }
                | 0x39uy ->
                    let value, nextRegisters = srl8 registers.C registers

                    { Cpu = { cpu with Registers = { nextRegisters with C = value; PC = registers.PC + 2us } }
                      Bus = bus
                      Cycles = 8 }
                | 0x3Auy ->
                    let value, nextRegisters = srl8 registers.D registers

                    { Cpu = { cpu with Registers = { nextRegisters with D = value; PC = registers.PC + 2us } }
                      Bus = bus
                      Cycles = 8 }
                | 0x3Buy ->
                    let value, nextRegisters = srl8 registers.E registers

                    { Cpu = { cpu with Registers = { nextRegisters with E = value; PC = registers.PC + 2us } }
                      Bus = bus
                      Cycles = 8 }
                | 0x3Cuy ->
                    let value, nextRegisters = srl8 registers.H registers

                    { Cpu = { cpu with Registers = { nextRegisters with H = value; PC = registers.PC + 2us } }
                      Bus = bus
                      Cycles = 8 }
                | 0x3Duy ->
                    let value, nextRegisters = srl8 registers.L registers

                    { Cpu = { cpu with Registers = { nextRegisters with L = value; PC = registers.PC + 2us } }
                      Bus = bus
                      Cycles = 8 }
                | 0x3Fuy ->
                    let value, nextRegisters = srl8 registers.A registers

                    { Cpu = { cpu with Registers = { nextRegisters with A = value; PC = registers.PC + 2us } }
                      Bus = bus
                      Cycles = 8 }
                | 0x47uy ->
                    let nextRegisters = bitTest 0 registers.A registers

                    { Cpu = { cpu with Registers = { nextRegisters with PC = registers.PC + 2us } }
                      Bus = bus
                      Cycles = 8 }
                | 0x40uy ->
                    let nextRegisters = bitTest 0 registers.B registers

                    { Cpu = { cpu with Registers = { nextRegisters with PC = registers.PC + 2us } }
                      Bus = bus
                      Cycles = 8 }
                | 0x42uy ->
                    let nextRegisters = bitTest 0 registers.D registers

                    { Cpu = { cpu with Registers = { nextRegisters with PC = registers.PC + 2us } }
                      Bus = bus
                      Cycles = 8 }
                | 0x46uy ->
                    let value = Bus.readByte (getHL registers) bus
                    let nextRegisters = bitTest 0 value registers

                    { Cpu = { cpu with Registers = { nextRegisters with PC = registers.PC + 2us } }
                      Bus = bus
                      Cycles = 12 }
                | 0x4Euy ->
                    let value = Bus.readByte (getHL registers) bus
                    let nextRegisters = bitTest 1 value registers

                    { Cpu = { cpu with Registers = { nextRegisters with PC = registers.PC + 2us } }
                      Bus = bus
                      Cycles = 12 }
                | 0x4Fuy ->
                    let nextRegisters = bitTest 1 registers.A registers

                    { Cpu = { cpu with Registers = { nextRegisters with PC = registers.PC + 2us } }
                      Bus = bus
                      Cycles = 8 }
                | 0x50uy ->
                    let nextRegisters = bitTest 2 registers.B registers

                    { Cpu = { cpu with Registers = { nextRegisters with PC = registers.PC + 2us } }
                      Bus = bus
                      Cycles = 8 }
                | 0x56uy ->
                    let value = Bus.readByte (getHL registers) bus
                    let nextRegisters = bitTest 2 value registers

                    { Cpu = { cpu with Registers = { nextRegisters with PC = registers.PC + 2us } }
                      Bus = bus
                      Cycles = 12 }
                | 0x57uy ->
                    let nextRegisters = bitTest 2 registers.A registers

                    { Cpu = { cpu with Registers = { nextRegisters with PC = registers.PC + 2us } }
                      Bus = bus
                      Cycles = 8 }
                | 0x58uy ->
                    let nextRegisters = bitTest 3 registers.B registers

                    { Cpu = { cpu with Registers = { nextRegisters with PC = registers.PC + 2us } }
                      Bus = bus
                      Cycles = 8 }
                | 0x5Euy ->
                    let value = Bus.readByte (getHL registers) bus
                    let nextRegisters = bitTest 3 value registers

                    { Cpu = { cpu with Registers = { nextRegisters with PC = registers.PC + 2us } }
                      Bus = bus
                      Cycles = 12 }
                | 0x5Fuy ->
                    let nextRegisters = bitTest 3 registers.A registers

                    { Cpu = { cpu with Registers = { nextRegisters with PC = registers.PC + 2us } }
                      Bus = bus
                      Cycles = 8 }
                | 0x60uy ->
                    let nextRegisters = bitTest 4 registers.B registers

                    { Cpu = { cpu with Registers = { nextRegisters with PC = registers.PC + 2us } }
                      Bus = bus
                      Cycles = 8 }
                | 0x66uy ->
                    let value = Bus.readByte (getHL registers) bus
                    let nextRegisters = bitTest 4 value registers

                    { Cpu = { cpu with Registers = { nextRegisters with PC = registers.PC + 2us } }
                      Bus = bus
                      Cycles = 12 }
                | 0x68uy ->
                    let nextRegisters = bitTest 5 registers.B registers

                    { Cpu = { cpu with Registers = { nextRegisters with PC = registers.PC + 2us } }
                      Bus = bus
                      Cycles = 8 }
                | 0x6Fuy ->
                    let nextRegisters = bitTest 5 registers.A registers

                    { Cpu = { cpu with Registers = { nextRegisters with PC = registers.PC + 2us } }
                      Bus = bus
                      Cycles = 8 }
                | 0x6Euy ->
                    let value = Bus.readByte (getHL registers) bus
                    let nextRegisters = bitTest 5 value registers

                    { Cpu = { cpu with Registers = { nextRegisters with PC = registers.PC + 2us } }
                      Bus = bus
                      Cycles = 12 }
                | 0x77uy ->
                    let nextRegisters = bitTest 6 registers.A registers

                    { Cpu = { cpu with Registers = { nextRegisters with PC = registers.PC + 2us } }
                      Bus = bus
                      Cycles = 8 }
                | 0x76uy ->
                    let value = Bus.readByte (getHL registers) bus
                    let nextRegisters = bitTest 6 value registers

                    { Cpu = { cpu with Registers = { nextRegisters with PC = registers.PC + 2us } }
                      Bus = bus
                      Cycles = 12 }
                | 0x7Fuy ->
                    let nextRegisters = bitTest 7 registers.A registers

                    { Cpu = { cpu with Registers = { nextRegisters with PC = registers.PC + 2us } }
                      Bus = bus
                      Cycles = 8 }
                | 0x7Euy ->
                    let value = Bus.readByte (getHL registers) bus
                    let nextRegisters = bitTest 7 value registers

                    { Cpu = { cpu with Registers = { nextRegisters with PC = registers.PC + 2us } }
                      Bus = bus
                      Cycles = 12 }
                | 0x86uy ->
                    let address = getHL registers
                    let value = Bus.readByte address bus &&& 0xFEuy
                    let bus = Bus.writeByte address value bus

                    { Cpu = { cpu with Registers = { registers with PC = registers.PC + 2us } }
                      Bus = bus
                      Cycles = 16 }
                | 0x87uy ->
                    { Cpu = { cpu with Registers = { registers with A = registers.A &&& 0xFEuy; PC = registers.PC + 2us } }
                      Bus = bus
                      Cycles = 8 }
                | 0x8Fuy ->
                    { Cpu = { cpu with Registers = { registers with A = registers.A &&& 0xFDuy; PC = registers.PC + 2us } }
                      Bus = bus
                      Cycles = 8 }
                | 0x97uy ->
                    { Cpu = { cpu with Registers = { registers with A = registers.A &&& 0xFBuy; PC = registers.PC + 2us } }
                      Bus = bus
                      Cycles = 8 }
                | 0xAFuy ->
                    { Cpu = { cpu with Registers = { registers with A = registers.A &&& 0xDFuy; PC = registers.PC + 2us } }
                      Bus = bus
                      Cycles = 8 }
                | 0x96uy ->
                    let address = getHL registers
                    let value = Bus.readByte address bus &&& 0xFBuy
                    let bus = Bus.writeByte address value bus

                    { Cpu = { cpu with Registers = { registers with PC = registers.PC + 2us } }
                      Bus = bus
                      Cycles = 16 }
                | 0x9Euy ->
                    let address = getHL registers
                    let value = Bus.readByte address bus &&& 0xF7uy
                    let bus = Bus.writeByte address value bus

                    { Cpu = { cpu with Registers = { registers with PC = registers.PC + 2us } }
                      Bus = bus
                      Cycles = 16 }
                | 0xA6uy ->
                    let address = getHL registers
                    let value = Bus.readByte address bus &&& 0xEFuy
                    let bus = Bus.writeByte address value bus

                    { Cpu = { cpu with Registers = { registers with PC = registers.PC + 2us } }
                      Bus = bus
                      Cycles = 16 }
                | 0xAEuy ->
                    let address = getHL registers
                    let value = Bus.readByte address bus &&& 0xDFuy
                    let bus = Bus.writeByte address value bus

                    { Cpu = { cpu with Registers = { registers with PC = registers.PC + 2us } }
                      Bus = bus
                      Cycles = 16 }
                | 0xD6uy ->
                    let address = getHL registers
                    let value = Bus.readByte address bus ||| 0x04uy
                    let bus = Bus.writeByte address value bus

                    { Cpu = { cpu with Registers = { registers with PC = registers.PC + 2us } }
                      Bus = bus
                      Cycles = 16 }
                | 0xDEuy ->
                    let address = getHL registers
                    let value = Bus.readByte address bus ||| 0x08uy
                    let bus = Bus.writeByte address value bus

                    { Cpu = { cpu with Registers = { registers with PC = registers.PC + 2us } }
                      Bus = bus
                      Cycles = 16 }
                | 0xE6uy ->
                    let address = getHL registers
                    let value = Bus.readByte address bus ||| 0x10uy
                    let bus = Bus.writeByte address value bus

                    { Cpu = { cpu with Registers = { registers with PC = registers.PC + 2us } }
                      Bus = bus
                      Cycles = 16 }
                | 0xFFuy ->
                    { Cpu = { cpu with Registers = { registers with A = registers.A ||| 0x80uy; PC = registers.PC + 2us } }
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
            | 0xE2uy ->
                let address = 0xFF00us + uint16 registers.C
                let bus = Bus.writeByte address registers.A bus

                { Cpu = { cpu with Registers = { registers with PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 8 }
            | 0xE5uy ->
                let sp, bus = write16ToStack (getHL registers) registers.SP bus

                { Cpu = { cpu with Registers = { registers with SP = sp; PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 16 }
            | 0xE1uy ->
                let value, sp = read16FromStack registers.SP bus
                let nextRegisters = registers |> setHL value

                { Cpu = { cpu with Registers = { nextRegisters with SP = sp; PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 12 }
            | 0xE9uy ->
                { Cpu = { cpu with Registers = { registers with PC = getHL registers } }
                  Bus = bus
                  Cycles = 4 }
            | 0xEAuy ->
                let address = readImmediate16 bus (registers.PC + 1us)
                let bus = Bus.writeByte address registers.A bus

                { Cpu = { cpu with Registers = { registers with PC = registers.PC + 3us } }
                  Bus = bus
                  Cycles = 16 }
            | 0xEFuy ->
                let sp, bus = write16ToStack (registers.PC + 1us) registers.SP bus

                { Cpu = { cpu with Registers = { registers with SP = sp; PC = 0x0028us } }
                  Bus = bus
                  Cycles = 16 }
            | 0xEEuy ->
                let value = Bus.readByte (registers.PC + 1us) bus
                let nextRegisters = xorA value registers

                { Cpu = { cpu with Registers = { nextRegisters with PC = registers.PC + 2us } }
                  Bus = bus
                  Cycles = 8 }
            | 0xF3uy ->
                { Cpu = { cpu with Registers = { registers with PC = registers.PC + 1us }; InterruptsEnabled = false }
                  Bus = bus
                  Cycles = 4 }
            | 0xF5uy ->
                let value = (uint16 registers.A <<< 8) ||| uint16 (registers.F &&& 0xF0uy)
                let sp, bus = write16ToStack value registers.SP bus

                { Cpu = { cpu with Registers = { registers with SP = sp; PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 16 }
            | 0xF0uy ->
                let offset = Bus.readByte (registers.PC + 1us) bus
                let address = 0xFF00us + uint16 offset
                let value = Bus.readByte address bus

                { Cpu = { cpu with Registers = { registers with A = value; PC = registers.PC + 2us } }
                  Bus = bus
                  Cycles = 12 }
            | 0xF1uy ->
                let value, sp = read16FromStack registers.SP bus
                let a = byte (value >>> 8)
                let f = byte value &&& 0xF0uy

                { Cpu = { cpu with Registers = { registers with A = a; F = f; SP = sp; PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 12 }
            | 0xF2uy ->
                let address = 0xFF00us + uint16 registers.C
                let value = Bus.readByte address bus

                { Cpu = { cpu with Registers = { registers with A = value; PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 8 }
            | 0xF6uy ->
                let value = Bus.readByte (registers.PC + 1us) bus
                let nextRegisters = orA value registers

                { Cpu = { cpu with Registers = { nextRegisters with PC = registers.PC + 2us } }
                  Bus = bus
                  Cycles = 8 }
            | 0xFAuy ->
                let address = readImmediate16 bus (registers.PC + 1us)
                let value = Bus.readByte address bus

                { Cpu = { cpu with Registers = { registers with A = value; PC = registers.PC + 3us } }
                  Bus = bus
                  Cycles = 16 }
            | 0xFBuy ->
                { Cpu = { cpu with Registers = { registers with PC = registers.PC + 1us }; InterruptsEnabled = true }
                  Bus = bus
                  Cycles = 4 }
            | 0xF8uy ->
                let offset = Bus.readByte (registers.PC + 1us) bus
                let signedOffset = if offset < 0x80uy then int offset else int offset - 0x100
                let result = uint16 (int registers.SP + signedOffset)
                let halfCarry = (registers.SP &&& 0x000Fus) + (uint16 offset &&& 0x000Fus) > 0x000Fus
                let carry = (registers.SP &&& 0x00FFus) + (uint16 offset &&& 0x00FFus) > 0x00FFus
                let nextRegisters =
                    registers
                    |> setHL result
                    |> setFlags false false halfCarry carry

                { Cpu = { cpu with Registers = { nextRegisters with PC = registers.PC + 2us } }
                  Bus = bus
                  Cycles = 12 }
            | 0xF9uy ->
                { Cpu = { cpu with Registers = { registers with SP = getHL registers; PC = registers.PC + 1us } }
                  Bus = bus
                  Cycles = 8 }
            | 0xFEuy ->
                let value = Bus.readByte (registers.PC + 1us) bus
                let nextRegisters = compareA value registers

                { Cpu = { cpu with Registers = { nextRegisters with PC = registers.PC + 2us } }
                  Bus = bus
                  Cycles = 8 }
            | unsupported ->
                raise (UnsupportedOpcode(unsupported, registers.PC))
