# Third-party notices

## IddSampleDriver (virtual display driver)

The files in [`driver/`](driver/) (`IddSampleDriver.inf`, `IddSampleDriver.dll`, `iddsampledriver.cat`, `Virtual_Display_Driver.cer`, `option.txt`, `installCert.bat`) are a prebuilt, self-signed community build of the **Indirect Display Driver sample** from Microsoft's [Windows-driver-samples](https://github.com/microsoft/Windows-driver-samples/tree/main/video/IndirectDisplay). That sample is published by Microsoft under the MIT License.

The community forks that add the `option.txt` resolution list and the self-signed certificate include:

- https://github.com/roshkins/IddSampleDriver
- https://github.com/ge9/IddSampleDriver

These files are redistributed unmodified, and their original licenses and copyright notices apply. Virtual Display Tray's own MIT license covers only the tray application, build scripts, icons and website in this repository.

### Microsoft Windows-driver-samples: MIT License

```
Copyright (c) Microsoft Corporation. All rights reserved.

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
