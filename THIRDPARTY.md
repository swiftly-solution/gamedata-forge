# Third Party Notices

## IDA SDK

`thirdparty/ida-sdk/` holds one directory per vendored SDK, named after its `major.minor` line.
Each contains headers vendored verbatim from the Hex-Rays IDA SDK under `include/`, exported-symbol
listings of the two runtime libraries under `exports/` so codegen is reproducible without those
libraries present, and a `VERSION` file naming the exact release. Together they are the input to
`GameData.IDA.Codegen`, which generates the bindings in `GameData.IDA/src/Core/Native/Generated/`.

- [GitHub Repository](https://github.com/HexRaysSA/ida-sdk)
- **Vendored versions**:
  - `9.2` — [`9.2.0-sdk.1`](https://github.com/HexRaysSA/ida-sdk/tree/v9.2.0-sdk.1)
  - `9.3` — [`9.3.0-sdk.3`](https://github.com/HexRaysSA/ida-sdk/tree/v9.3.0-sdk.3)

```
MIT License

Copyright (c) 2025  Hex-Rays SA

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## DepotDownloader

- [GitHub Repository](https://github.com/SteamRE/DepotDownloader/tree/1e8e20c72cad64deeb70443a758dda5b750bb119)
- **Git Commit**: `1e8e20c72cad64deeb70443a758dda5b750bb119`

```
                    GNU GENERAL PUBLIC LICENSE
                       Version 2, June 1991

 Copyright (C) 1989, 1991 Free Software Foundation, Inc.,
 51 Franklin Street, Fifth Floor, Boston, MA 02110-1301 USA
 Everyone is permitted to copy and distribute verbatim copies
 of this license document, but changing it is not allowed.

             How to Apply These Terms to Your New Programs

 If you develop a new program, and you want it to be of the greatest
 possible use to the public, the best way to achieve this is to make it
 free software which everyone can redistribute and change under these terms.

 To do so, attach the following notices to the program.  It is safest
 to attach them to the start of each source file to most effectively
 convey the exclusion of warranty; and each file should have at least
 the "copyright" line and a pointer to where the full notice is found.

    <one line to give the program's name and a brief idea of what it does.>
    Copyright (C) <year>  <name of author>

    This program is free software; you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation; either version 2 of the License, or
    (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License along
    with this program; if not, write to the Free Software Foundation, Inc.,
    51 Franklin Street, Fifth Floor, Boston, MA 02110-1301 USA.
```