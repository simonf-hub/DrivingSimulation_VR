// Phone held up like a window into the car (DrivingPractice.TryTilt): turning and tilting it looks around.
// 1. deviceorientation (preferred): the phone's own fused orientation, smooth and drift-corrected. W3C order:
//    R = Rz(alpha) Rx(beta) Ry(gamma), earth frame x east, y north, z up; the camera looks out of the phone's
//    back, -z in phone axes, so look = R * (0,0,-1).
// 2. devicemotion (fallback, when no orientation arrives): gyroscope turn rate about gravity, integrated; pitch
//    from gravity. Noisier and it drifts; the game's Ahead button re-centres.
// Inside an embedding page (itch.io) phone browsers send neither, so on a phone in a frame a "Tap to play" screen
// opens the game's own page in a new tab. On that page, a fullscreen button (phones that support it).
// iPhone Safari only sends motion after requestPermission(), which must run in a tap: the first tap asks once.
// Browsers only send motion on HTTPS pages (itch.io is HTTPS).
mergeInto(LibraryManager.library, {
  DeviceTiltStart: function () {
    if (window.bcTilt) return;
    var t = window.bcTilt = { hasO: 0, oHeading: 0, oPitch: 0, hasM: 0, heading: 0, gx: 0, gy: 0, gz: 0, last: 0 };
    var d = Math.PI / 180;

    function onOrientation(e) {
      if (e.alpha === null || e.beta === null || e.gamma === null) return;
      var a = e.alpha * d, b = e.beta * d, g = e.gamma * d;
      var east = -Math.sin(g) * Math.cos(a) - Math.cos(g) * Math.sin(b) * Math.sin(a);
      var north = -Math.sin(g) * Math.sin(a) + Math.cos(g) * Math.sin(b) * Math.cos(a);
      var up = -Math.cos(b) * Math.cos(g);
      t.oHeading = Math.atan2(east, north) / d;
      t.oPitch = Math.asin(Math.max(-1, Math.min(1, up))) / d;
      t.hasO = 1;
    }

    function onMotion(e) {
      var r = e.rotationRate, g = e.accelerationIncludingGravity;
      if (!r || !g || r.alpha === null || g.x === null) return;
      var now = performance.now();
      var dt = t.last ? Math.min((now - t.last) / 1000, 0.1) : 0;
      t.last = now;
      var k = t.hasM ? 0.1 : 1; // smoothed "up" in phone axes
      t.gx += (g.x - t.gx) * k; t.gy += (g.y - t.gy) * k; t.gz += (g.z - t.gz) * k;
      var len = Math.sqrt(t.gx * t.gx + t.gy * t.gy + t.gz * t.gz);
      if (len < 1) return;
      t.heading -= (r.beta * t.gx + r.gamma * t.gy + r.alpha * t.gz) / len * dt; // turn rate about up, clockwise
      t.hasM = 1;
    }

    function listen() {
      window.addEventListener('deviceorientation', onOrientation);
      window.addEventListener('devicemotion', onMotion);
    }
    var needsPermission = typeof DeviceOrientationEvent !== 'undefined' && typeof DeviceOrientationEvent.requestPermission === 'function';
    if (needsPermission) {
      var ask = function () {
        document.removeEventListener('touchend', ask);
        document.removeEventListener('click', ask);
        DeviceOrientationEvent.requestPermission().then(function (s) { if (s === 'granted') listen(); }).catch(function () {});
        if (typeof DeviceMotionEvent !== 'undefined' && DeviceMotionEvent.requestPermission) DeviceMotionEvent.requestPermission().catch(function () {});
      };
      document.addEventListener('touchend', ask);
      document.addEventListener('click', ask);
    } else listen();

    if (!(navigator.maxTouchPoints > 0)) return; // the rest is for phones

    function button(text, css) {
      var b = document.createElement('button');
      b.textContent = text;
      b.style.cssText = css;
      return b;
    }

    if (window.self !== window.top) {
      // Embedded: motion is withheld here. Offer the game's own page.
      var cover = document.createElement('div');
      cover.style.cssText = 'position:fixed;inset:0;z-index:9999;display:flex;flex-direction:column;align-items:center;' +
        'justify-content:center;gap:18px;background:rgba(14,24,34,.92);font-family:sans-serif;color:#fff;text-align:center';
      var play = button('Tap to play', 'padding:16px 40px;font:700 24px sans-serif;color:#14202c;background:#f2c230;border:0;border-radius:30px');
      var note = document.createElement('div');
      note.textContent = 'Opens in a new tab. Hold your phone up and turn it to look around.';
      note.style.cssText = 'font-size:15px;opacity:.85;max-width:80%';
      var stay = document.createElement('a');
      stay.textContent = 'Play here instead (swipe to look)';
      stay.href = '#';
      stay.style.cssText = 'font-size:14px;color:#9fc3e6';
      play.onclick = function () { window.open(location.href, '_blank'); cover.remove(); };
      stay.onclick = function (e) { e.preventDefault(); cover.remove(); };
      cover.appendChild(play); cover.appendChild(note); cover.appendChild(stay);
      document.body.appendChild(cover);
      return;
    }

    // Own page: fullscreen (and landscape lock where allowed). Browsers only allow it from a tap, so the first tap
    // anywhere goes fullscreen; a button (top left) brings it back after leaving fullscreen.
    if (!document.fullscreenEnabled) return; // iPhone Safari: no page fullscreen
    function goFullscreen() {
      if (document.fullscreenElement) return;
      document.documentElement.requestFullscreen().then(function () {
        if (screen.orientation && screen.orientation.lock) screen.orientation.lock('landscape').catch(function () {});
      }).catch(function () {});
    }
    var firstTap = function () { document.removeEventListener('touchend', firstTap); goFullscreen(); };
    document.addEventListener('touchend', firstTap);
    var full = button('Fullscreen', 'position:fixed;left:12px;top:12px;z-index:9999;padding:10px 16px;font:600 15px sans-serif;' +
      'color:#fff;background:rgba(18,30,40,.8);border:0;border-radius:18px');
    full.onclick = goFullscreen;
    document.addEventListener('fullscreenchange', function () { full.style.display = document.fullscreenElement ? 'none' : ''; });
    document.body.appendChild(full);
  },
  DeviceTiltHasData: function () { var t = window.bcTilt; return t && (t.hasO || t.hasM) ? 1 : 0; },
  DeviceTiltHeading: function () { var t = window.bcTilt; return !t ? 0 : t.hasO ? t.oHeading : t.heading; },
  // Degrees the phone's back (the "camera") points above the horizon: 0 with the phone held upright.
  DeviceTiltPitch: function () {
    var t = window.bcTilt;
    if (!t) return 0;
    if (t.hasO) return t.oPitch;
    var len = Math.sqrt(t.gx * t.gx + t.gy * t.gy + t.gz * t.gz);
    return len < 1 ? 0 : Math.asin(Math.max(-1, Math.min(1, -t.gz / len))) * 180 / Math.PI;
  }
});
