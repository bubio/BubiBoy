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

    let private vramByte address (memory: Bus.Memory) =
        Bus.rawVramByte address memory

    let private paletteShade palette colorNumber =
        int ((palette >>> (colorNumber * 2)) &&& 0x03uy)

    let private pixelColor palette colorNumber =
        DmgColors[paletteShade palette colorNumber]

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

    let private tilePixel memory tileAddress column =
        let low = vramByte tileAddress memory
        let high = vramByte (tileAddress + 1) memory
        let shift = 7 - column
        let lowBit = (low >>> shift) &&& 0x01uy
        let highBit = (high >>> shift) &&& 0x01uy
        int (lowBit ||| (highBit <<< 1))

    let private backgroundTileMapBase lcdc =
        if bitSet 3 lcdc then 0x9C00 else 0x9800

    let private windowTileMapBase lcdc =
        if bitSet 6 lcdc then 0x9C00 else 0x9800

    type private Sprite =
        { Index: int
          X: int
          Y: int
          TileIndex: byte
          Attributes: byte }

    let private spriteOnLine spriteHeight y sprite =
        let yInSprite = y - sprite.Y
        yInSprite >= 0 && yInSprite < spriteHeight

    let private lineSprites (memory: Bus.Memory) spriteHeight y =
        seq {
            for spriteIndex in 0 .. 39 do
                let baseIndex = spriteIndex * 4
                let sprite =
                    { Index = spriteIndex
                      Y = int (Bus.rawOamByte baseIndex memory) - 16
                      X = int (Bus.rawOamByte (baseIndex + 1) memory) - 8
                      TileIndex = Bus.rawOamByte (baseIndex + 2) memory
                      Attributes = Bus.rawOamByte (baseIndex + 3) memory }

                if spriteOnLine spriteHeight y sprite then
                    sprite
        }
        |> Seq.truncate 10
        |> Seq.sortWith (fun left right ->
            match compare right.X left.X with
            | 0 -> compare right.Index left.Index
            | result -> result)
        |> Seq.toArray

    let private renderBackgroundPixel memory lcdc x y =
        if not (bitSet 0 lcdc) then
            0
        else
            let scx = int (io 0x43 memory)
            let scy = int (io 0x42 memory)
            let sourceX = (x + scx) &&& 0xFF
            let sourceY = (y + scy) &&& 0xFF
            let tileColumn = sourceX / 8
            let tileRow = sourceY / 8
            let tileIndex = vramByte (backgroundTileMapBase lcdc + tileRow * 32 + tileColumn) memory
            let address = tileAddress lcdc tileIndex (sourceY % 8)

            tilePixel memory address (sourceX % 8)

    let private renderWindowPixel memory lcdc x y =
        let wx = int (io 0x4B memory) - 7
        let wy = int (io 0x4A memory)

        if bitSet 5 lcdc && y >= wy && x >= wx then
            let sourceX = x - wx
            let sourceY = y - wy

            if sourceX < 256 && sourceY < 256 then
                let tileColumn = sourceX / 8
                let tileRow = sourceY / 8
                let tileIndex = vramByte (windowTileMapBase lcdc + tileRow * 32 + tileColumn) memory
                let address = tileAddress lcdc tileIndex (sourceY % 8)
                Some(tilePixel memory address (sourceX % 8))
            else
                None
        else
            None

    let private renderSpriteLine (memory: Bus.Memory) lcdc y (backgroundShades: byte[]) (framebuffer: uint32[]) =
        if bitSet 1 lcdc then
            let spriteHeight = if bitSet 2 lcdc then 16 else 8

            for sprite in lineSprites memory spriteHeight y do
                let tileIndex =
                    if spriteHeight = 16 then
                        sprite.TileIndex &&& 0xFEuy
                    else
                        sprite.TileIndex

                let attributes = sprite.Attributes
                let palette = if bitSet 4 attributes then io 0x49 memory else io 0x48 memory
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
                            let colorNumber = tilePixel memory address sourceX

                            if colorNumber <> 0 then
                                let pixelIndex = y * Hardware.ScreenWidth + x
                                let backgroundIsOpaque = backgroundShades[pixelIndex] <> 0uy

                                if not behindBackground || not backgroundIsOpaque then
                                    framebuffer[pixelIndex] <- pixelColor palette colorNumber

    let renderScanline y memory (framebuffer: uint32[]) =
        let lcdc = io 0x40 memory

        if y >= 0 && y < Hardware.ScreenHeight then
            let lineStart = y * Hardware.ScreenWidth

            if bitSet 7 lcdc then
                let backgroundShades = Array.zeroCreate<byte> FramebufferPixels
                let bgp = io 0x47 memory

                for x in 0 .. Hardware.ScreenWidth - 1 do
                    let colorNumber =
                        match renderWindowPixel memory lcdc x y with
                        | Some windowColor -> windowColor
                        | None -> renderBackgroundPixel memory lcdc x y

                    let pixelIndex = lineStart + x
                    backgroundShades[pixelIndex] <- byte colorNumber
                    framebuffer[pixelIndex] <- pixelColor bgp colorNumber

                renderSpriteLine memory lcdc y backgroundShades framebuffer
            else
                for x in 0 .. Hardware.ScreenWidth - 1 do
                    framebuffer[lineStart + x] <- DmgColors[0]

    let renderFrame memory =
        let lcdc = io 0x40 memory
        let framebuffer = blankFrame ()

        if bitSet 7 lcdc then
            for y in 0 .. Hardware.ScreenHeight - 1 do
                renderScanline y memory framebuffer

        framebuffer
