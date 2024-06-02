# EcXR
For Unity. Creates platform-agnostic XR rig that lives between scene loads

## Package Requirements
- Unity XR Interaction toolkit package (`Unity.XR.Interaction.Toolkit`)
### Optional:
- [Oculus Core or All-In-One SDK (`com.meta.xr.sdk.core')](https://assetstore.unity.com/packages/tools/integration/meta-xr-core-sdk-269169)

## How to use:
Download the source zip and drag into Unity project or clone this repository into your project Asset folder.

The `XROriginLoader` script instantiates the prefab in `Resources/XR Origin` and marks it to not disappear when scenes load. You do not have to add this script to any object in any scene as it runs when the application first launches.

Package also includes a `PassthroughManager` script to toggle passthrough on different headset platforms. Only Oculus is supported for now, but any other platforms we use will be added in the future.
