// The global summary and each type own their columns; every selected metric sorts fastest first.
function sortMetric(root, head) {
  const table = head.closest('table');
  const direction = head.dataset.seconds === 'true' ? 'ascending' : 'descending';
  for (const column of table.querySelectorAll('thead th[data-metric]')) {
    column.setAttribute('aria-sort', column === head ? direction : 'none');
  }
  const value = row => row.querySelector(`td[data-metric="${head.dataset.metric}"]`).getAttribute('data-sort-value');
  const rows = [...table.tBodies[0].rows].sort((a, b) => {
    const left = value(a), right = value(b);
    if (left === null || right === null) return (right !== null) - (left !== null);
    return direction === 'ascending' ? Number(left) - Number(right) : Number(right) - Number(left);
  });
  table.tBodies[0].append(...rows);
  const label = head.querySelector('button').firstChild.textContent;
  const message = `Sorted by ${label}, fastest first (1 node). Failed and missing values follow. Select a column to sort. Swipe or scroll horizontally to see all columns.`;
  const section = table.closest('section');
  section.querySelector('.table-scroll-hint').textContent = message;
  const count = rows.filter(row => !row.hidden).length;
  root.querySelector('#bench-announcement').textContent = `${section.querySelector('h3').textContent} · ${count} comparison rows. ${message}`;
}

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
  const sections = [...results.querySelectorAll('.bench-overview')];
  const show = () => {
    const value = controls.querySelector('input:checked').value;
    for (const section of sections) section.hidden = section.dataset.type !== value;
    const section = sections.find(section => !section.hidden);
    sortMetric(root, section.querySelector('th[aria-sort]:not([aria-sort="none"])'));
    shadeScrollRegions(results);
  };
  const sort = event => {
    const button = event.target.closest('.sort-case');
    if (button) sortMetric(root, button.closest('th'));
  };
  const shade = () => shadeScrollRegions(results);
  const resize = new ResizeObserver(shade);
  controls.addEventListener('change', show);
  results.addEventListener('click', sort);
  results.addEventListener('scroll', shade, { capture: true, passive: true });
  resize.observe(results);
  controls.hidden = false;
  show();
  return {
    dispose() {
      controls.removeEventListener('change', show);
      results.removeEventListener('click', sort);
      results.removeEventListener('scroll', shade, { capture: true });
      resize.disconnect();
    },
  };
}
