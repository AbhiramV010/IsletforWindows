Make a photoreal launch video for Islet (this repo: a Dynamic Island-style overlay for Windows, WPF/.NET 8).
Output: showcase.mp4 in the repo root, 1920x1080, 24 fps, 24 s, H.264 + AAC. Work in brag-output/ and delete that
folder when the video is done and verified. Downloads are approved: npm packages, Poly Haven assets, and the
OpenGameArt track below. The goal is "REAL real life": it must look like footage of a real ThinkPad on a real
desk, not an animation.

APPROACH (proven to work; follow it)
- Path-traced three.js in headless Chrome, driven frame by frame by a Node + puppeteer-core script. Do NOT use
  Hyperframes for capture: path tracing needs seconds per frame and an async accumulate loop.
- npm (inside brag-output/tools): three@0.186.1, three-gpu-pathtracer@0.0.27, three-mesh-bvh@0.9.16,
  puppeteer-core, ffmpeg-static, ffprobe-static. ffmpeg-static's install script is blocked by npm
  allow-scripts, so run `node node_modules/ffmpeg-static/install.js` by hand.
- Chrome: use a cached chrome-headless-shell (e.g. ~/.cache/hyperframes/chrome/...), or download one with
  `npx @puppeteer/browsers install chrome-headless-shell@stable`. Launch args: --use-angle=d3d11
  --ignore-gpu-blocklist --enable-gpu --disable-gpu-watchdog. Set protocolTimeout 0.
- Serve the repo root with a tiny Node static server on 127.0.0.1. The page lives at
  brag-output/scene/index.html and uses an import map:
  three -> three/build/three.module.js, three/webgpu -> three.webgpu.js, three/examples/jsm/ and three/addons/ ->
  three/examples/jsm/, three-mesh-bvh -> its build/index.module.js, three-gpu-pathtracer -> its build/index.module.js.
- Use WebGLPathTracer (deprecated but works in WebGL2):
  - renderDelay=0, fadeDuration=0, minSamples=0, renderToCanvas=false, rasterizeScene=false.
  - tiles 4x4: keeps each GPU draw short, avoids Windows TDR context loss.
  - bounces 4–6, filterGlossyFactor 1.0, textureSize 2048x2048.
- Per frame, window.renderAt(t, spp) does:
  1. Set scene state; updateCamera().
  2. reset(), then loop renderSample() until samples >= spp.
  3. IMPORTANT: shader compilation only starts on the first renderSample and is async. While
     pathTracer.isCompiling, await and don't count the iteration, or you get 0 samples.
  4. Yield with setTimeout(0) every 4 samples.
  5. Draw pathTracer.target.texture to the canvas with a FullScreenQuad(DenoiseMaterial{sigma≈3.2,
     threshold≈0.28}). It tone-maps.
  6. renderer.render(overlayScene, camera) with autoClear=false, for the sharp screen layer.
  7. gl.readPixels 1px to sync.
  The driver screenshots each frame to frames/fNNNN.png (skip existing files, so the render can resume), then
  ffmpeg encodes.
- Speed reference: on an AMD 780M iGPU, 24 spp at renderScale 0.667 took ~12 s/frame. On a 3060 Ti use
  renderScale 1.0 and 48–64 spp. In the close-up (t 6.5–18.3) the screen overlay covers most of the frame,
  so lower renderScale/spp there.

SCENE (units: centimetres)
- Environment: Poly Haven "poly_haven_studio" 4k HDR (CC0), a real office with windows. Use it as both
  scene.environment and scene.background, intensity 0.9, environmentRotation.y ≈ 200° (match
  backgroundRotation). Load with three/addons/loaders/HDRLoader.js. ACESFilmic tone mapping, exposure 1.0,
  sRGB output.
- Desk: Poly Haven "oak_veneer_01" 2k diff + nor_gl (CC0). Use Box 170x3x95 with its top at y=0, centred at
  z=-5, so the back edge shows the blurred room behind. Texture repeat 1 tile per 62 cm, roughness 0.62
  constant (NO roughness map: it caused fireflies), normalScale 0.3, color tint 0xf0e6da.
- Props:
  - A deep-blue glazed mug, color 0x1d3f8a, roughness 0.32. A white mug was too noisy. It's a LatheGeometry
    about 8.4 cm wide and 9.5 cm tall, with coffee inside and a torus handle, at (-30, 0, -12).
  - A black notebook, about 14.8x1.3x21 with a paper block and an elastic band, at (-33, 0, 9),
    rotated 0.32 rad.
- Laptop: a 14" ThinkPad T-series, whole group rotated y -6°.
  - Base: RoundedBox 32.4x1.8x22.7, radius 0.6, on 4 rubber feet (0.25 lift). Chassis 0x1b1b1e, roughness
    0.74 (no noise maps: they sparkled).
  - Keyboard: 6 rows of RoundedBox keys, pitch 1.85, Fn at bottom-left, ThinkPad layout, with a white legend
    decal (MeshStandard + transparent map; the path tracer supports only Standard/Physical materials).
  - Red TrackPoint between G/H/B, three TrackPoint buttons with red stripes, trackpad 11.8x6.4
    (roughness 0.6), a palm-rest "ThinkPad" wordmark with the red dot on the i, hinge cylinder.
  - Lid opened ~108°: rotation.x -18° about the hinge at (0, 1.75, -10.9). Lid 32.4x21.6x0.55, matte black
    bezel 0x0b0b0c, webcam.
  - 16:10 display, 30x18.75 cm. Its bottom edge is 1.95 cm above the lid bottom.
- Screen, two layers:
  (a) Path-traced: a glossy black emitter (Standard color 0x050505, roughness 0.22, emissiveMap = a 960x600
      copy of the desktop, emissiveIntensity ≈ 1.6). It lights the keyboard and reflects the room.
  (b) Raster overlay drawn on top: MeshBasic, toneMapped false, depthTest false, opacity 0.93 so glass
      reflections show through. It holds the 3840x2400 desktop canvas, the island canvas (480x100 native px
      x7) and a cursor. It's parented to a group whose matrix = lid.matrixWorld.
- Desktop content (1920x1200 native):
  - Windows 11 blue "bloom" wallpaper.
  - Windows Terminal at (400, 230, 1120x760) with a ">_ claude" tab, showing Claude Code:
    - A "✻ Welcome to Claude Code!" box with "cwd: C:\dev\my-app".
    - "> run the tests and fix what fails"
    - "● I'll run the test suite first."
    - "● Bash(npm test)" (sky blue #7dd3fc)
    - Then one of these lines, by time:
      - Until 10.6 s: "⎿ Waiting for permission…"
      - From 10.6 s: "⎿ 42 passed, 0 failed (1.8s)"
      - From 11.6 s, an extra line: "● All 42 tests pass. Nothing needed fixing."
  - Dark Windows 11 taskbar: centred icons, terminal underlined, "10:24 AM / 10/17/2026".
  - Font: src/Islet.App/Fonts/OverpassMono.ttf. Await document.fonts.load before painting.
- The island:
  - An inverted isosceles trapezoid flush with the top edge of the screen: fill rgba(13,13,17,.97), 1px white
    0.5 border, 5px rounded bottom corners. It must look exactly like the real app (see
    src/Islet.App/Ui/TrapezoidBorder and the OverlayWindow views).
  - States, as w/h/bottom-inset in native px: idle 220/22/12 with a red 72x2.4 line, compact 340/30/16,
    perm 460/73/18, card 460/61/15.
  - Morphs (ease-out cubic):
    - 6.34 s: idle → compact (0.5 s)
    - 8.96 s: compact → perm (0.55 s)
    - 10.62 s: perm → compact (0.45 s)
    - 11.6 s: compact → card (0.5 s)
    - 15.81 s: card → compact (0.4 s)
    - 18.44 s: compact → idle (0.6 s)
  - Content layers, crossfaded:
    - idle: until 6.34 and from 18.6.
    - music, 6.34–7.92 and 16.86–18.44: "Night Drive  Neon Harbor", a blue gradient art tile and a sky pause icon.
    - agent, 7.92–8.96 and 10.7–11.6: "Claude Code · my-app" and "Working · Bash/Edit".
    - perm, 8.96–10.62: "Claude Code · my-app" / "Bash  npm test", Deny and Allow pills (Allow in sky
      #7dd3fc) and "Enter allow · Esc deny". Allow is pressed at 10.54 with a small scale dip.
    - done, 11.6–14.76: "Claude is done  Go back to your session".
    - toast, 14.76–15.81: "Calendar / Standup  in 5 minutes".
    - clock, 15.81–16.86: a stopwatch icon and "12:34".
- Cursor: a white arrow, 30 native px. Visible 9.3–11.25 s. It moves from (1500,340) to (1109,27) native
  over 9.45–10.45 s (ease in-out) and clicks Allow at 10.54.

CAMERA (PhysicalCamera)
- Setup: fov 30, apertureBlades 7. focusDistance = the distance to the focus point. GOTCHA: the path tracer
  reads aperture in metres, so in a cm scene set camera.fStop = realF / 100.
- Shots (pos → target):
  - open: (-14, 34, 88) → (-1, 6, -3), f/2.8, focus on the screen centre.
  - wide: (-3, 29, 60) → (0, 8, -4), f/2.8.
  - zoom: anchor = lid-local point (0, displayTop - 4.1, front), position = anchor + lidNormal*19.5, f/4.
  - zoomDrift: anchor + normal*18.0 + (0.7, 0, 0).
  - end: (21, 33, 86) → (21, 8, -1), f/2.4. The laptop sits left so the end card fits on the right.
  - endDrift: (19.8, 32.4, 83.8).
- Timeline:
  - 0–3.7 s: open → wide, easeInOutQuad.
  - 3.7–6.34 s: wide → zoom, easeInOutCubic.
  - 6.34–18.44 s: zoom → zoomDrift, linear.
  - 18.44–21.06 s: zoomDrift → end, easeInOutCubic.
  - 21.06–24 s: end → endDrift, ease-out.
- Handheld sway: add amp * (sin(0.71t+k)*.5 + sin(1.37t+2.1k)*.3 + sin(2.93t+.7k)*.2) to the position and
  (×0.4) to the target. amp is 0.35 cm in wide shots and 0.07 in the close-up.
- Check the close-up framing: the island sits near the top, the terminal's left and right edges are visible,
  and no noisy strip of room shows above the bezel.

TEXT (DOM over the canvas; keep it minimal, like a real ad)
- Hook: "Windows never got a Dynamic Island." Shown 0.5–3.4 s, bottom centre, white 44px Overpass Mono 600
  with a soft shadow.
- End card, from 21.1 s at the right:
  - "Islet": white, 200px, 700.
  - A red #e8262b rule.
  - "A Dynamic Island for Windows."
  - "Claude Code · Codex · Music · Notifications"
- Also: a subtle CSS vignette and a 4.5% overlay film grain (seeded, moving per frame).
- No other captions. Determinism: seeded random only (mulberry32), no Date/Math.random.

AUDIO
- Music: "First Light Particles" by Yoiyami, CC0, from
  https://opengameart.org/sites/default/files/first_light_particles_0.wav. Use the first 24 s with a 0.4 s
  fade-in and a fade-out from 21.2 s over 2.8 s.
- A soft trackpad click at 10.54 s at low volume (any CC0 UI click, e.g. Kenney's click_003).
- loudnorm to I=-16 LUFS, TP -1.5. Verify with ebur128 (-16 ±1). Mux as AAC 48 kHz stereo.
- Earlier "no music" reports were the player not decoding AAC. Also make an MP3-in-MP4 variant if asked.

VERIFY, THEN DELIVER
- First test-render 3 frames (1.5 s, 10.2 s, 20 s) and inspect them for:
  - noise and fireflies (raise spp or tune materials, not the denoise strength)
  - framing
  - legible island text
  - the terminal content matching the island state
- Full render, then encode: ffmpeg libx264 -crf 17 -preset slow -pix_fmt yuv420p -movflags +faststart, with
  the soundtrack.
- Bake the best frame (the end card at ~23 s) as frame 0: overlay a still on n=0. Then check that the streams
  are h264 + aac.
- Copy to ./showcase.mp4, delete brag-output/, and reply with short share copy.
