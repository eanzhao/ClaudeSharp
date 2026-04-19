// Shared atoms / chrome components for Aevatar Service Workbench
// All components read from window.* globals (no JSX import resolution).

const { useState, useEffect, useMemo, useRef, useCallback } = React;

// ----- Icons (tiny SVGs, monochrome) -----
const Icon = {
  Workflow: (p) => React.createElement('svg', {viewBox:'0 0 20 20', width:14, height:14, fill:'none', stroke:'currentColor', strokeWidth:1.5, ...p},
    React.createElement('circle',{cx:4,cy:4,r:2}), React.createElement('circle',{cx:4,cy:16,r:2}),
    React.createElement('circle',{cx:16,cy:10,r:2}),
    React.createElement('path',{d:'M6 4h4a2 2 0 0 1 2 2v0M6 16h4a2 2 0 0 0 2-2v0'})),
  Script: (p) => React.createElement('svg', {viewBox:'0 0 20 20', width:14, height:14, fill:'none', stroke:'currentColor', strokeWidth:1.5, ...p},
    React.createElement('path',{d:'M5 3h7l3 3v11a1 1 0 0 1-1 1H5a1 1 0 0 1-1-1V4a1 1 0 0 1 1-1z'}),
    React.createElement('path',{d:'M12 3v3h3'}),
    React.createElement('path',{d:'M7 10l-1.5 2L7 14M13 10l1.5 2L13 14M10 9l-1 6'})),
  GAgent: (p) => React.createElement('svg', {viewBox:'0 0 20 20', width:14, height:14, fill:'none', stroke:'currentColor', strokeWidth:1.5, ...p},
    React.createElement('rect',{x:5,y:4,width:10,height:12,rx:2}),
    React.createElement('circle',{cx:8,cy:9,r:0.8,fill:'currentColor'}),
    React.createElement('circle',{cx:12,cy:9,r:0.8,fill:'currentColor'}),
    React.createElement('path',{d:'M8 13h4'})),
  External: (p) => React.createElement('svg', {viewBox:'0 0 20 20', width:14, height:14, fill:'none', stroke:'currentColor', strokeWidth:1.5, ...p},
    React.createElement('path',{d:'M4 10a6 6 0 0 1 12 0 6 6 0 0 1-12 0z'}),
    React.createElement('path',{d:'M4 10h12M10 4a9 9 0 0 1 0 12M10 4a9 9 0 0 0 0 12'})),
  Play: (p) => React.createElement('svg', {viewBox:'0 0 20 20', width:12, height:12, fill:'currentColor', ...p},
    React.createElement('path',{d:'M6 4l10 6-10 6z'})),
  Check: (p) => React.createElement('svg', {viewBox:'0 0 20 20', width:12, height:12, fill:'none', stroke:'currentColor', strokeWidth:2, ...p},
    React.createElement('path',{d:'M4 10l4 4 8-9'})),
  Copy: (p) => React.createElement('svg', {viewBox:'0 0 20 20', width:12, height:12, fill:'none', stroke:'currentColor', strokeWidth:1.5, ...p},
    React.createElement('rect',{x:7,y:7,width:9,height:9,rx:1.5}),
    React.createElement('path',{d:'M13 7V5a1 1 0 0 0-1-1H5a1 1 0 0 0-1 1v7a1 1 0 0 0 1 1h2'})),
  Chevron: (p) => React.createElement('svg', {viewBox:'0 0 20 20', width:10, height:10, fill:'none', stroke:'currentColor', strokeWidth:2, ...p},
    React.createElement('path',{d:'M7 5l5 5-5 5'})),
  Caret: (p) => React.createElement('svg', {viewBox:'0 0 20 20', width:10, height:10, fill:'currentColor', ...p},
    React.createElement('path',{d:'M5 7l5 6 5-6z'})),
  Dot: (p) => React.createElement('svg', {viewBox:'0 0 20 20', width:8, height:8, fill:'currentColor', ...p},
    React.createElement('circle',{cx:10,cy:10,r:4})),
  Plus: (p) => React.createElement('svg', {viewBox:'0 0 20 20', width:12, height:12, fill:'none', stroke:'currentColor', strokeWidth:2, ...p},
    React.createElement('path',{d:'M10 4v12M4 10h12'})),
  Settings: (p) => React.createElement('svg', {viewBox:'0 0 20 20', width:14, height:14, fill:'none', stroke:'currentColor', strokeWidth:1.5, ...p},
    React.createElement('circle',{cx:10,cy:10,r:3}),
    React.createElement('path',{d:'M10 2v2M10 16v2M2 10h2M16 10h2M4.2 4.2l1.5 1.5M14.3 14.3l1.5 1.5M4.2 15.8l1.5-1.5M14.3 5.7l1.5-1.5'})),
  User: (p) => React.createElement('svg', {viewBox:'0 0 20 20', width:14, height:14, fill:'none', stroke:'currentColor', strokeWidth:1.5, ...p},
    React.createElement('circle',{cx:10,cy:7,r:3}),
    React.createElement('path',{d:'M4 17c1-3 3-4 6-4s5 1 6 4'})),
};
window.Icon = Icon;

// ----- Small helpers -----
function classNames(){ return [...arguments].filter(Boolean).join(' '); }
window.cx = classNames;

function formatMs(ms){
  if (ms < 1000) return `${ms}ms`;
  if (ms < 60000) return `${(ms/1000).toFixed(2)}s`;
  const s = Math.floor(ms/1000); return `${Math.floor(s/60)}m ${s%60}s`;
}
window.formatMs = formatMs;

function useCopyToClipboard(){
  const [copied, setCopied] = useState(false);
  const copy = useCallback((text) => {
    try {
      navigator.clipboard?.writeText(text);
    } catch(e){}
    setCopied(true);
    setTimeout(()=>setCopied(false), 1200);
  }, []);
  return [copied, copy];
}
window.useCopyToClipboard = useCopyToClipboard;

// ----- Provenance pill -----
function Prov({kind='live', children}){
  return React.createElement('span', {className:`prov ${kind}`}, children || kind.toUpperCase());
}
window.Prov = Prov;

// ----- Kind label -----
function KindBadge({kind}){
  const map = {
    workflow: {icon: Icon.Workflow, label:'workflow', cls:'accent'},
    script:   {icon: Icon.Script,   label:'script',   cls:'copper'},
    gagent:   {icon: Icon.GAgent,   label:'gagent',   cls:''},
    external: {icon: Icon.External, label:'external', cls:''},
  };
  const m = map[kind] || map.gagent;
  return React.createElement('span',{className:`pill ${m.cls}`}, React.createElement(m.icon), m.label);
}
window.KindBadge = KindBadge;

// ----- Health dot -----
function HealthDot({state}){
  const color = {healthy:'var(--ok)', degraded:'var(--warn)', attention:'var(--warn)', error:'var(--err)', unknown:'var(--ink-4)'}[state] || 'var(--ink-4)';
  return React.createElement('span',{
    title: state,
    style:{display:'inline-block',width:8,height:8,borderRadius:2,background:color,marginRight:6,verticalAlign:'middle'}
  });
}
window.HealthDot = HealthDot;

// ----- Copy button -----
function CopyBtn({text, label}){
  const [copied, copy] = useCopyToClipboard();
  return React.createElement('button', {className:'btn small', onClick:()=>copy(text)},
    copied ? React.createElement(Icon.Check) : React.createElement(Icon.Copy),
    copied ? 'copied' : (label||'copy'));
}
window.CopyBtn = CopyBtn;
