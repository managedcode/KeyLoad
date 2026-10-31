// The tables are static markup; the page script only filters data types and shades scrollable edges.

// The pinned edge shades once columns pass beneath it; the far edge fades while more columns remain.
export function shadeScrollRegions(container) {
  for (const frame of container.querySelectorAll('.scroll-frame')) {
    const scroll = frame.firstElementChild;
    frame.classList.toggle('more-start', scroll.scrollLeft > 1);
    frame.classList.toggle('more-end', scroll.scrollWidth - scroll.clientWidth - scroll.scrollLeft > 1);
  }
}

export function mountResultsControls(root) {
  const controls = root.querySelector('.bench-controls');
  const results = root.querySelector('#bench-results');
  const announcement = root.querySelector('#bench-announcement');
  const sections = [...results.querySelectorAll('section[data-type]')];
  const show = () => {
    const value = controls.querySelector('input:checked').value;
    for (const section of sections) section.hidden = value !== 'all' && section.dataset.type !== value;
    shadeScrollRegions(results);
    const shown = sections.filter(section => !section.hidden);
    const rows = shown.reduce((sum, section) => sum + section.querySelectorAll('.ov-row').length, 0);
    announcement.textContent = `${value === 'all' ? 'All data types' : shown[0].dataset.label} · ${rows} comparison rows.`;
  };
  // Edge shades follow scrolling, viewport changes and opening a recorded-values table.
  const shade = () => shadeScrollRegions(results);
  const resize = new ResizeObserver(shade);
  controls.addEventListener('change', show);
  results.addEventListener('scroll', shade, { capture: true, passive: true });
  resize.observe(results);
  controls.hidden = false;
  show();
  return {
    dispose() {
      controls.removeEventListener('change', show);
      results.removeEventListener('scroll', shade, { capture: true });
      resize.disconnect();
    },
  };
}
