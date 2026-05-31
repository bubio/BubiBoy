namespace BubiBoy.Core

module Video =
    [<Literal>]
    let FramebufferPixels = Hardware.ScreenWidth * Hardware.ScreenHeight

    let DmgColors: uint32[] =
        [| 0xFFE0F8D0u
           0xFF88C070u
           0xFF346856u
           0xFF081820u |]

    let blankFrame () =
        Array.create FramebufferPixels DmgColors[0]

    let private bitSet bit value =
        value &&& (1uy <<< bit) <> 0uy

    let private io index (memory: Bus.Memory) =
        Bus.rawIoByte index memory

    let private paletteShade palette colorNumber =
        int ((palette >>> (colorNumber * 2)) &&& 0x03uy)

    let private pixelColor palette colorNumber =
        DmgColors[paletteShade palette colorNumber]

    let private cgbColor (paletteRamByte: int -> Bus.Memory -> byte) palette colorNumber memory =
        let index = palette * 8 + colorNumber * 2
        let low = uint16 (paletteRamByte index memory)
        let high = uint16 (paletteRamByte (index + 1) memory)
        let raw = low ||| (high <<< 8)
        let scale value = uint32 ((value * 255 + 15) / 31)
        let red = scale (int (raw &&& 0x001Fus))
        let green = scale (int ((raw >>> 5) &&& 0x001Fus))
        let blue = scale (int ((raw >>> 10) &&& 0x001Fus))
        0xFF000000u ||| (red <<< 16) ||| (green <<< 8) ||| blue

    let private unsignedTileAddress tileIndex row =
        0x8000 + int tileIndex * 16 + row * 2

    let private signedTileAddress tileIndex row =
        let signedIndex =
            if tileIndex < 128uy then
                int tileIndex
            else
                int tileIndex - 256

        0x9000 + signedIndex * 16 + row * 2

    let private tileAddress lcdc tileIndex row =
        if bitSet 4 lcdc then
            unsignedTileAddress tileIndex row
        else
            signedTileAddress tileIndex row

    let private tilePixel memory bank tileAddress column =
        let low = Bus.rawVramBankByte bank tileAddress memory
        let high = Bus.rawVramBankByte bank (tileAddress + 1) memory
        let shift = 7 - column
        let lowBit = (low >>> shift) &&& 0x01uy
        let highBit = (high >>> shift) &&& 0x01uy
        int (lowBit ||| (highBit <<< 1))

    let private backgroundTileMapBase lcdc =
        if bitSet 3 lcdc then 0x9C00 else 0x9800

    let private windowTileMapBase lcdc =
        if bitSet 6 lcdc then 0x9C00 else 0x9800

    [<Struct>]
    type private Sprite =
        { Index: int
          X: int
          Y: int
          TileIndex: byte
          Attributes: byte }

    [<Struct>]
    type private BackgroundPixel =
        { ColorNumber: int
          Palette: int
          Priority: bool }

    type RenderScratch =
        private
            { Sprites: Sprite[]
              BackgroundShades: byte[]
              BackgroundPriority: bool[] }

    let createScratch () =
        { Sprites = Array.zeroCreate<Sprite> 10
          BackgroundShades = Array.zeroCreate<byte> Hardware.ScreenWidth
          BackgroundPriority = Array.zeroCreate<bool> Hardware.ScreenWidth }

    let private threadScratch =
        new System.Threading.ThreadLocal<RenderScratch>(fun () -> createScratch ())

    let private coordinateSpritePriority memory =
        Bus.mode memory = Hardware.Dmg || io 0x6C memory &&& 0x01uy <> 0uy

    // Ordering that reproduces the previous Seq.sortWith / Seq.sortByDescending: returns
    // a positive value when `a` should be drawn before `b` (later draws win on screen).
    let private compareSprites coordinatePriority (a: Sprite) (b: Sprite) =
        if coordinatePriority then
            match compare b.X a.X with
            | 0 -> compare b.Index a.Index
            | result -> result
        else
            compare b.Index a.Index

    // Fills `sprites` with up to 10 on-line sprites (first 10 by OAM index, matching the
    // hardware limit), ordered for drawing, and returns how many were written. No seq,
    // enumerator, or intermediate sort allocations.
    let private collectLineSprites (memory: Bus.Memory) spriteHeight y (sprites: Sprite[]) =
        let mutable count = 0
        let mutable spriteIndex = 0

        while spriteIndex <= 39 && count < 10 do
            let baseIndex = spriteIndex * 4
            let spriteY = int (Bus.rawOamByte baseIndex memory) - 16
            let yInSprite = y - spriteY

            if yInSprite >= 0 && yInSprite < spriteHeight then
                sprites[count] <-
                    { Index = spriteIndex
                      Y = spriteY
                      X = int (Bus.rawOamByte (baseIndex + 1) memory) - 8
                      TileIndex = Bus.rawOamByte (baseIndex + 2) memory
                      Attributes = Bus.rawOamByte (baseIndex + 3) memory }
                count <- count + 1

            spriteIndex <- spriteIndex + 1

        let coordinatePriority = coordinateSpritePriority memory

        for i in 1 .. count - 1 do
            let current = sprites[i]
            let mutable j = i - 1

            while j >= 0 && compareSprites coordinatePriority sprites[j] current > 0 do
                sprites[j + 1] <- sprites[j]
                j <- j - 1

            sprites[j + 1] <- current

        count

    let private renderBackgroundPixel memory lcdc x y =
        if not (bitSet 0 lcdc) then
            { ColorNumber = 0; Palette = 0; Priority = false }
        else
            let scx = int (io 0x43 memory)
            let scy = int (io 0x42 memory)
            let sourceX = (x + scx) &&& 0xFF
            let sourceY = (y + scy) &&& 0xFF
            let tileColumn = sourceX / 8
            let tileRow = sourceY / 8
            let tileMapAddress = backgroundTileMapBase lcdc + tileRow * 32 + tileColumn
            let tileIndex = Bus.rawVramBankByte 0 tileMapAddress memory
            let attributes =
                if Bus.mode memory = Hardware.Cgb then
                    Bus.rawVramBankByte 1 tileMapAddress memory
                else
                    0uy

            let row = if bitSet 6 attributes then 7 - (sourceY % 8) else sourceY % 8
            let column = if bitSet 5 attributes then 7 - (sourceX % 8) else sourceX % 8
            let address = tileAddress lcdc tileIndex row

            { ColorNumber = tilePixel memory (int ((attributes >>> 3) &&& 0x01uy)) address column
              Palette = int (attributes &&& 0x07uy)
              Priority = bitSet 7 attributes }

    // Returns the window pixel when the window covers (x, y); otherwise the background
    // pixel. Folding the old Option-returning renderWindowPixel into the caller avoids a
    // per-pixel Some allocation (BackgroundPixel is now a struct).
    let private renderBackgroundOrWindowPixel memory lcdc x y =
        let wx = int (io 0x4B memory) - 7
        let wy = int (io 0x4A memory)

        if bitSet 5 lcdc && y >= wy && x >= wx && x - wx < 256 && y - wy < 256 then
            let sourceX = x - wx
            let sourceY = y - wy
            let tileColumn = sourceX / 8
            let tileRow = sourceY / 8
            let tileMapAddress = windowTileMapBase lcdc + tileRow * 32 + tileColumn
            let tileIndex = Bus.rawVramBankByte 0 tileMapAddress memory
            let attributes =
                if Bus.mode memory = Hardware.Cgb then
                    Bus.rawVramBankByte 1 tileMapAddress memory
                else
                    0uy

            let row = if bitSet 6 attributes then 7 - (sourceY % 8) else sourceY % 8
            let column = if bitSet 5 attributes then 7 - (sourceX % 8) else sourceX % 8
            let address = tileAddress lcdc tileIndex row

            { ColorNumber = tilePixel memory (int ((attributes >>> 3) &&& 0x01uy)) address column
              Palette = int (attributes &&& 0x07uy)
              Priority = bitSet 7 attributes }
        else
            renderBackgroundPixel memory lcdc x y

    let private renderSpriteLine (memory: Bus.Memory) lcdc y (scratch: RenderScratch) (framebuffer: uint32[]) =
        if bitSet 1 lcdc then
            let spriteHeight = if bitSet 2 lcdc then 16 else 8
            let sprites = scratch.Sprites
            let count = collectLineSprites memory spriteHeight y sprites

            for spriteSlot in 0 .. count - 1 do
                let sprite = sprites[spriteSlot]
                let tileIndex =
                    if spriteHeight = 16 then
                        sprite.TileIndex &&& 0xFEuy
                    else
                        sprite.TileIndex

                let attributes = sprite.Attributes
                let palette = if bitSet 4 attributes then io 0x49 memory else io 0x48 memory
                let cgbPalette = int (attributes &&& 0x07uy)
                let cgbTileBank = int ((attributes >>> 3) &&& 0x01uy)
                let xFlip = bitSet 5 attributes
                let yFlip = bitSet 6 attributes
                let behindBackground = bitSet 7 attributes

                let yInSprite = y - sprite.Y

                if yInSprite >= 0 && yInSprite < spriteHeight then
                    let sourceY =
                        if yFlip then
                            spriteHeight - 1 - yInSprite
                        else
                            yInSprite

                    let tileOffset = if spriteHeight = 16 && sourceY >= 8 then 1 else 0
                    let row = sourceY % 8
                    let address = unsignedTileAddress (tileIndex + byte tileOffset) row

                    for xInSprite in 0 .. 7 do
                        let x = sprite.X + xInSprite

                        if x >= 0 && x < Hardware.ScreenWidth then
                            let sourceX = if xFlip then 7 - xInSprite else xInSprite
                            let colorNumber = tilePixel memory cgbTileBank address sourceX

                            if colorNumber <> 0 then
                                let pixelIndex = y * Hardware.ScreenWidth + x
                                let backgroundIsOpaque = scratch.BackgroundShades[x] <> 0uy
                                let backgroundWins = scratch.BackgroundPriority[x] && backgroundIsOpaque

                                if not backgroundWins && (not behindBackground || not backgroundIsOpaque) then
                                    framebuffer[pixelIndex] <-
                                        if Bus.mode memory = Hardware.Cgb then
                                            cgbColor Bus.rawObjPaletteByte cgbPalette colorNumber memory
                                        else
                                            pixelColor palette colorNumber

    let renderScanlineWithScratch y memory (framebuffer: uint32[]) scratch =
        let lcdc = io 0x40 memory

        if y >= 0 && y < Hardware.ScreenHeight then
            let lineStart = y * Hardware.ScreenWidth

            if bitSet 7 lcdc then
                let bgp = io 0x47 memory

                for x in 0 .. Hardware.ScreenWidth - 1 do
                    let backgroundPixel = renderBackgroundOrWindowPixel memory lcdc x y

                    let pixelIndex = lineStart + x
                    scratch.BackgroundShades[x] <- byte backgroundPixel.ColorNumber
                    scratch.BackgroundPriority[x] <- backgroundPixel.Priority
                    framebuffer[pixelIndex] <-
                        if Bus.mode memory = Hardware.Cgb then
                            cgbColor Bus.rawBgPaletteByte backgroundPixel.Palette backgroundPixel.ColorNumber memory
                        else
                            pixelColor bgp backgroundPixel.ColorNumber

                renderSpriteLine memory lcdc y scratch framebuffer
            else
                for x in 0 .. Hardware.ScreenWidth - 1 do
                    framebuffer[lineStart + x] <- DmgColors[0]

    let renderScanline y memory (framebuffer: uint32[]) =
        renderScanlineWithScratch y memory framebuffer (createScratch ())

    let renderScanlineReusable y memory (framebuffer: uint32[]) =
        renderScanlineWithScratch y memory framebuffer threadScratch.Value

    let renderFrame memory =
        let lcdc = io 0x40 memory
        let framebuffer = blankFrame ()
        let scratch = createScratch ()

        if bitSet 7 lcdc then
            for y in 0 .. Hardware.ScreenHeight - 1 do
                renderScanlineWithScratch y memory framebuffer scratch

        framebuffer
