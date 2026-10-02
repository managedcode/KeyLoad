import { DataViews, Dom, Id, View, ViewSections } from './constants.js';
import { Text } from './text.js';
import { el, write } from './dom.js';

const sections = [...new Set(Object.values(ViewSections))];

export function selectView(view) {
    write(Id.title, Text.titles[view]);
    write(Id.breadcrumb, Text.titles[view]);
    write(Id.breadcrumbGroup, Text.groups[view]);
    write(Id.description, Text.descriptions[view]);
    sections.forEach(section => { el(section).hidden = section !== ViewSections[view]; });
    el(Id.scopeBar).hidden = !DataViews.includes(view);
    document.querySelectorAll(Dom.nav).forEach(button => {
        button.removeAttribute(Dom.current);
        if (button.dataset.view === view) button.setAttribute(Dom.current, Dom.page);
    });
    closeMenu();
}

export function closeMenu() {
    el(Id.menu).closest(Dom.aside).classList.remove(Dom.open);
    el(Id.menu).setAttribute(Dom.expanded, Dom.false);
}

export function initializeNavigation(navigate) {
    document.querySelectorAll(Dom.nav).forEach(button => button.addEventListener(Dom.click, () => navigate(button.dataset.view)));
    document.querySelectorAll(Dom.goto).forEach(button => button.addEventListener(Dom.click, () => navigate(button.dataset.goto)));
    el(Id.menu).addEventListener(Dom.click, () => {
        const sidebar = el(Id.menu).closest(Dom.aside);
        const open = !sidebar.classList.contains(Dom.open);
        sidebar.classList.toggle(Dom.open, open);
        el(Id.menu).setAttribute(Dom.expanded, String(open));
    });
}

export const isDataView = view => DataViews.includes(view) && view !== View.catalog;
