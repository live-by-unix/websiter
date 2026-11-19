document.addEventListener('DOMContentLoaded', async () => {
  const el = document.getElementById('worker-data');
  if (!el) return;
  try {
    const res = await fetch('/api/workers');
    el.textContent = JSON.stringify(await res.json(), null, 2);
  } catch (e) {
    el.textContent = 'Failed to load worker data: ' + e.message;
  }
});
