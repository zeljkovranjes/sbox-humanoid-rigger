# C# hand inference

The editor uses MediaPipe's hand landmark model as an optional anatomical prior.
All rendering, inference integration, 3D fusion, scoring and tests are C#.
There is no Python process or Python dependency.

The [OpenCV Zoo hand model](https://github.com/opencv/opencv_zoo/tree/25f423d0e04c31a17254620e58febd7386da523b/models/handpose_estimation_mediapipe)
is an upstream-published ONNX conversion of MediaPipe's TFLite hand model.
The float model is 4,099,621 bytes; the lower-accuracy int8 variants are not used.
Its license is Apache 2.0 (see `mediapipe-Apache-2.0.txt`).
Input is float RGB, NHWC `[1,224,224,3]`, scaled to `[0,1]`, with fingers oriented upward.
Outputs are 21 image-space landmarks, hand presence, handedness, and 21 world-space landmarks.
The implementation uses presence rather than handedness as the prediction confidence.
Neither learned depth nor learned metric world coordinates become final joint coordinates.

The native CPU backend is [ONNX Runtime 1.23.2](https://github.com/microsoft/onnxruntime/releases/tag/v1.23.2).
`NativeHandModel` binds the first stable portion of its [version 23 C API](https://github.com/microsoft/onnxruntime/blob/v1.23.2/include/onnxruntime/core/session/onnxruntime_c_api.h).
The named table offsets were checked against that header. Native tensors, sessions,
environments and allocated names are released by their owning instance.
See `onnxruntime-LICENSE.txt` and `onnxruntime-ThirdPartyNotices.txt`.

`HandModelAssets` downloads pinned upstream artifacts into the tool's own local
application-data cache and verifies SHA-256 before loading either model or native DLL.
No character geometry or rendered images are uploaded. Download/runtime failures
retain geometric detection and record a diagnostic instead of failing rig creation.

| Artifact | SHA-256 |
| --- | --- |
| Hand ONNX | `db0898ae717b76b075d9bf563af315b29562e11f8df5027a1ef07b02bef6d81c` |
| ONNX Runtime release ZIP | `0b38df9af21834e41e73d602d90db5cb06dbd1ca618948b8f1d66d607ac9f3cd` |
| ONNX Runtime DLL | `dec964ab1ee36cc9b0ae247d13b376627992fc57dec0454354017ab8fd84f1ea` |

The six hand crops retain front/back mesh depth. A proposal needs multiple different
view directions, agreement with the segmented digit, a plausible chain, and a better
combined score than the current geometric chain. Final joints are refined to local
mesh centers. Geometric fingertip extrema and manually corrected points are retained.
Nonstandard or inseparable digit topology uses geometry; a five-digit model never
adds a missing digit. Canonical roles are refined before any target-profile mapping.
