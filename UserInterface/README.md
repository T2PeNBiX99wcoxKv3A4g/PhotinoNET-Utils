# @photino-net-utils/bridge

The `window.external` request/response bridge shared by the Photino front-ends, plus the global
`External` type declaration.

It is consumed as a local package, without a build step:

```json
{
  "dependencies": {
    "@photino-net-utils/bridge": "file:../../../PhotinoNETUtils/UserInterface"
  }
}
```

```ts
import {Native} from "@photino-net-utils/bridge";

const info = await Native.request<{version: string}>("app.info");

const off = Native.on((message) => {
    if (message.type === "app.navigate") console.log(message.data);
});
```

`Native.available` is false in a plain browser, so UI code can fall back to browser behaviour
during `npm run dev` without the desktop host.
