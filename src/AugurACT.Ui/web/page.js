'use strict';

// Page features beyond the timeline. A feature registers once, from its own file, and the page places it:
//
//   window.AugurPage.register({
//     key: 'eventTexts',
//     tabs: [{ key, label, render(container) }],   // a tab of its own, rendered again whenever the data changes
//     recordSection(record) -> Node,                // under each encounter on the Encounters data tab
//   });
//
// Features talk to the plugin through AugurPage.ctx, set by app.js before any of this runs:
// { send(message), host (true inside ACT), getData() (the last "encounter-data" message) }.
// Timeline marks, list columns and side panels are mechanics instead (AugurTimeline.register).
window.AugurPage = (() => {
  const features = [];
  return {
    features,
    ctx: null,
    register(feature) {
      features.push(feature);
    },
  };
})();
