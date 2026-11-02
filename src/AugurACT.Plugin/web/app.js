'use strict';

const table = document.getElementById('encounters');
const empty = document.getElementById('empty');

function formatDuration(summary) {
  if (summary.startEpoch === null || summary.endEpoch === null) return '-';
  const seconds = summary.endEpoch - summary.startEpoch;
  return `${Math.floor(seconds / 60)}:${String(seconds % 60).padStart(2, '0')}`;
}

function addEncounter(summary) {
  const row = table.tBodies[0].insertRow(0);
  const cells = [
    summary.title,
    summary.zone,
    formatDuration(summary),
    `${summary.parsedLines} / ${summary.totalLines}`,
  ];
  for (const text of cells) row.insertCell().textContent = text;
  table.hidden = false;
  empty.hidden = true;
}

window.chrome.webview.addEventListener('message', (event) => {
  if (event.data.type === 'encounter-summary') addEncounter(event.data);
});
