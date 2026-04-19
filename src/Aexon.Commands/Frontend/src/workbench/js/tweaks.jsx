// Tweaks floating panel + host wiring

function Tweaks({tweaks, setTweaks, onClose}){
  const set = (k,v) => { const next = {...tweaks, [k]:v}; setTweaks(next);
    window.dispatchEvent(new CustomEvent('tweaks-change',{detail:next})); };
  const Row = ({label, children}) => React.createElement('div',{className:'col gap-1',style:{marginBottom:12}},
    React.createElement('span',{className:'t-label'}, label),
    React.createElement('div',{className:'row gap-1', style:{flexWrap:'wrap'}}, children));
  const B = ({active, onClick, children}) => React.createElement('button',{className:'btn small', onClick,
    style:{background: active?'var(--ink-0)':'transparent', color: active?'var(--paper-0)':'var(--ink-1)', borderColor: active?'var(--ink-0)':'var(--hairline)'}}, children);

  return React.createElement('div',{style:{position:'fixed', top:58, right:14, width:296, zIndex:50, background:'var(--paper-0)', border:'1px solid var(--hairline)', borderRadius:10, boxShadow:'var(--shadow-2)'}},
    React.createElement('div',{className:'row gap-2 hair-b', style:{padding:'10px 12px'}},
      React.createElement(Icon.Settings),
      React.createElement('span',{className:'t-title', style:{fontSize:13}}, 'Tweaks'),
      React.createElement('div',{className:'grow'}),
      React.createElement('button',{className:'btn ghost small', onClick:onClose}, '×')),
    React.createElement('div',{style:{padding:'12px 12px 4px'}},
      React.createElement(Row,{label:'Accent'},
        [['blue','restrained blue'],['copper','copper'],['celadon','celadon']].map(([k,l])=>
          React.createElement(B,{key:k, active:tweaks.accent===k, onClick:()=>set('accent',k)}, l))),
      React.createElement(Row,{label:'Theme'},
        ['light','dark'].map(k=>
          React.createElement(B,{key:k, active:tweaks.theme===k, onClick:()=>set('theme',k)}, k))),
      React.createElement(Row,{label:'Density'},
        ['cozy','compact'].map(k=>
          React.createElement(B,{key:k, active:tweaks.density===k, onClick:()=>set('density',k)}, k))),
      React.createElement(Row,{label:'AGUI mode'},
        ['timeline','trace','tabs','bubbles','raw'].map(k=>
          React.createElement(B,{key:k, active:tweaks.aguiMode===k, onClick:()=>set('aguiMode',k)}, k))),
      React.createElement(Row,{label:'Invoke layout'},
        [['split','split'],['stack','stacked'],['canvas','canvas + history']].map(([k,l])=>
          React.createElement(B,{key:k, active:tweaks.layout===k, onClick:()=>set('layout',k)}, l))),
      React.createElement('div',{className:'t-hint', style:{fontSize:11, padding:'6px 0 10px'}},
        'State honesty, NyxID auth, and AGUI streaming are always on in this prototype.')
    )
  );
}
window.Tweaks = Tweaks;
