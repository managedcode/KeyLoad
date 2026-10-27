import { DOM, ENGINE_TYPES, TYPE_ORDER, assertIsolated } from './isolated-contracts.mjs';

export function isolatedElements(root) {
  const elements = Object.fromEntries(Object.entries(DOM).map(([key, id]) => [key, root.querySelector('#' + id)]));
  assertIsolated(Object.values(elements).every(Boolean));
  return elements;
}

export function initializeIsolatedControls(elements) {
  const fieldset = elements.type;
  const document = fieldset.ownerDocument;
  fieldset.querySelector('.chip-row')?.remove();
  const row = fieldset.appendChild(document.createElement('span'));
  row.className = 'chip-row';
  const engines = Object.values(ENGINE_TYPES);
  for (const value of ['all', ...TYPE_ORDER]) {
    const chip = row.appendChild(document.createElement('label'));
    const input = chip.appendChild(document.createElement('input'));
    const text = chip.appendChild(document.createElement('span'));
    chip.className = 'chip';
    input.type = 'radio';
    input.name = fieldset.id;
    input.value = value;
    input.checked = value === 'all';
    text.textContent = value === 'all' ? 'All databases' : value;
    text.appendChild(document.createElement('small')).textContent =
      String(value === 'all' ? engines.length : engines.filter(type => type === value).length);
  }
  elements.announcement.setAttribute('role', 'status');
  elements.announcement.setAttribute('aria-live', 'polite');
  elements.error.setAttribute('role', 'alert');
  elements.retry.type = 'button';
}

export const readIsolatedType = elements => elements.type.querySelector('input:checked').value;

export function selectIsolatedType(elements, value) {
  const input = [...elements.type.querySelectorAll('input')].find(item => item.value === value);
  if (input) input.checked = true;
  return Boolean(input);
}
