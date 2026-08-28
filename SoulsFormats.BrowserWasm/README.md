# SoulsFormats Browser WASM

This project (`SoulsFormats.BrowserWasm`) compiles `SoulsFormats` to WebAssembly so it can be used directly from JavaScript in browsers, NextJS, or Node.js.

## How to use in NextJS

1. Build this project by running:
   ```bash
   dotnet build -c Release
   ```
2. Copy the contents of `bin/Release/net9.0/wwwroot/_framework/` to your NextJS `public/_framework/` directory.
3. In your JavaScript/TypeScript code, you can load and interact with SoulsFormats like this:

```javascript
import { dotnet } from '/_framework/dotnet.js';

let exports = null;

async function initSoulsFormats() {
    if (!exports) {
        const { getAssemblyExports, getConfig } = await dotnet
            .withDiagnosticTracing(false)
            .create();
        const config = getConfig();
        exports = await getAssemblyExports(config.mainAssemblyName);
    }
    return exports;
}

export async function readBnd4(byteArray) {
    const api = await initSoulsFormats();
    
    // Reads any supported format by its class name (e.g. 'BND4', 'DCX', 'PARAM')
    const jsonResult = api.JSInterop.ReadSoulsFile('BND4', byteArray);
    
    const parsed = JSON.parse(jsonResult);
    if (parsed.error) {
        throw new Error(parsed.error);
    }
    return parsed;
}

export async function writeBnd4(bnd4Object) {
    const api = await initSoulsFormats();
    
    // Convert your JS object back to JSON
    const jsonStr = JSON.stringify(bnd4Object);
    
    // Returns a Uint8Array containing the compiled file
    return api.JSInterop.WriteSoulsFile('BND4', jsonStr);
}
```

## How it works

The interop relies on JSON serialization to communicate between C# and JS. 
When you read a file, it parses the byte array and serializes the resulting object to JSON. Byte arrays inside the C# objects (like file contents in a BND4) are automatically converted to Base64 strings in JSON.
When writing, you pass a JSON representation (with Base64 strings for byte arrays) and it will deserialize to the C# object and call `Write()`.

You can parse any format simply by passing its class name (e.g. `"DCX"`, `"BND4"`, `"PARAM"`).
