// Applies the saved theme and text direction before first paint, to avoid a flash.
// Kept in its own file so the page needs no inline script (see the Content-Security-Policy in nginx.conf).
try {
  var t = localStorage.getItem('tahona.theme');
  if (t === 'dark' || (!t && window.matchMedia('(prefers-color-scheme: dark)').matches)) document.documentElement.classList.add('dark');
  var l = localStorage.getItem('tahona.lang');
  if (l === 'ar') { document.documentElement.lang = 'ar'; document.documentElement.dir = 'rtl'; }
} catch { /* storage unavailable: keep the defaults */ }
