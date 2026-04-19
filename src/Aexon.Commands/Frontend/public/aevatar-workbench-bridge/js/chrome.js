// Top chrome + service list sidebar + service header

function TopBar({onToggleTweaks, tweaksOpen}){
  return React.createElement('div', {className:'topbar'},
    React.createElement('div', {className:'row gap-3', style:{padding:'0 14px', height:44}},
      React.createElement('div', {style:{width:24,height:24,borderRadius:6,background:'var(--ink-0)', color:'var(--paper-0)', display:'flex',alignItems:'center',justifyContent:'center',fontWeight:700,fontSize:12}}, 'A'),
      React.createElement('span', {className:'t-title', style:{fontSize:14}}, 'Aevatar Console'),
      React.createElement('span', {className:'t-hint'}, '/'),
      React.createElement('span', {className:'pill'},
        React.createElement('span',{style:{width:6,height:6,borderRadius:1,background:'var(--copper)'}}),
        'team · ', React.createElement('b',{style:{color:'var(--ink-0)'}}, 'support-ops')),
      React.createElement('span', {className:'t-hint'}, '→'),
      React.createElement('span', {className:'t-title', style:{fontSize:14}}, 'Services'),
      React.createElement('div',{className:'grow'}),
      React.createElement('span',{className:'prov delayed'}, 'cluster · staging · delayed 4s'),
      React.createElement('span',{className:'dot-sep'}),
      React.createElement('button', {className:'btn ghost small'}, 'Docs'),
      React.createElement('button', {className:`btn small ${tweaksOpen?'primary':''}`, onClick:onToggleTweaks},
        React.createElement(Icon.Settings), 'Tweaks'),
      React.createElement('div',{style:{width:1,height:20,background:'var(--hairline)',margin:'0 4px'}}),
      React.createElement('div', {className:'row gap-2', style:{padding:'2px 10px 2px 4px', borderRadius:999, background:'var(--paper-1)', border:'1px solid var(--hairline)'}},
        React.createElement('div',{style:{width:22,height:22,borderRadius:999,background:'linear-gradient(135deg,#c7a27a,#8f5a2b)',color:'#fff',display:'flex',alignItems:'center',justifyContent:'center',fontSize:10,fontWeight:600}},'JY'),
        React.createElement('span',{style:{fontSize:12}},'jade.yu'),
        React.createElement('span',{className:'pill copper', style:{padding:'0 6px',fontSize:10}},'nyxid · did:nyx:a1c…3f')
      )
    )
  );
}
window.TopBar = TopBar;

// ----- Service list sidebar -----
function ServiceList({services, activeId, onSelect, onCreate}){
  return React.createElement('div',{className:'svclist', style:{display:'flex', flexDirection:'column'}},
    React.createElement('div',{className:'row gap-2 hair-b', style:{padding:'10px 12px'}},
      React.createElement('span',{className:'t-label'}, 'Services'),
      React.createElement('span',{className:'pill', style:{padding:'0 6px',fontSize:10}}, services.length),
      React.createElement('div',{className:'grow'}),
      React.createElement('button',{className:'btn small primary', onClick:onCreate}, React.createElement(Icon.Plus), 'New')),
    React.createElement('div',{style:{padding:'8px 10px'}},
      React.createElement('input',{className:'input', placeholder:'Filter…', style:{width:'100%', height:26}})),
    React.createElement('div',{className:'grow', style:{overflow:'auto', padding:'2px 6px 8px'}},
      services.map(s => {
        const active = s.id === activeId;
        return React.createElement('button',{
          key:s.id, onClick:()=>onSelect(s.id),
          className:'svcrow',
          style:{
            display:'flex', alignItems:'flex-start', gap:10,
            width:'100%', textAlign:'left',
            padding:'10px 10px', marginBottom:2,
            background: active ? 'var(--accent-wash)' : 'transparent',
            border:'1px solid ' + (active ? 'var(--accent-wash-2)' : 'transparent'),
            borderRadius:8, cursor:'pointer'
          }},
          React.createElement('div',{style:{marginTop:3}},
            React.createElement(HealthDot,{state:s.health})),
          React.createElement('div',{style:{flex:1, minWidth:0}},
            React.createElement('div',{className:'row gap-2'},
              React.createElement('span',{style:{fontWeight:active?600:500, color:'var(--ink-0)', fontSize:13, overflow:'hidden', textOverflow:'ellipsis', whiteSpace:'nowrap'}}, s.display),
            ),
            React.createElement('div',{className:'row gap-2', style:{marginTop:3}},
              React.createElement(KindBadge,{kind:s.type}),
              React.createElement('span',{className:'t-hint', style:{fontSize:11}}, s.revision),
            ),
            React.createElement('div',{className:'row gap-2', style:{marginTop:4}},
              s.binding==='bound'  && React.createElement('span',{className:'pill ok',   style:{padding:'0 6px',fontSize:10}}, 'bound'),
              s.binding==='draft'  && React.createElement('span',{className:'pill warn', style:{padding:'0 6px',fontSize:10}}, 'draft'),
              s.binding==='unbound'&& React.createElement('span',{className:'pill',      style:{padding:'0 6px',fontSize:10,color:'var(--ink-3)'}}, 'unbound'),
              React.createElement('span',{className:'t-hint', style:{fontSize:11}}, s.lastRun)
            )
          )
        );
      })
    ),
    // Footer scope info
    React.createElement('div',{className:'hair-t', style:{padding:'10px 12px', fontSize:11, color:'var(--ink-3)'}},
      React.createElement('div',{className:'row gap-2'},
        React.createElement('span',{className:'t-label', style:{fontSize:10}}, 'Scope'),
        React.createElement('span',{className:'t-mono'}, 'sc-38f2'),
      ),
      React.createElement('div',{className:'row gap-2', style:{marginTop:4}},
        React.createElement(Prov,{kind:'live'}, 'runtime · live'),
      ),
      React.createElement('div',{className:'row gap-2', style:{marginTop:4}},
        React.createElement(Prov,{kind:'delayed'}, 'audit · 4s delay'),
      )
    )
  );
}
window.ServiceList = ServiceList;

// ----- Service header with step nav (Build / Bind / Invoke / Observe) -----
function Stepper({steps, active, onChange, service}){
  return React.createElement('div',{className:'stepper row gap-2', style:{padding:'0 16px 0 4px'}},
    steps.map((s, i) => {
      const on = s.key === active;
      const done = s.done;
      return React.createElement(React.Fragment, {key:s.key},
        i>0 && React.createElement('div',{style:{width:18, height:1, background:'var(--hairline)'}}),
        React.createElement('button',{
          onClick:()=>onChange(s.key),
          style:{
            display:'flex', alignItems:'center', gap:8,
            background: on ? 'var(--ink-0)' : 'transparent',
            color: on ? 'var(--paper-0)' : done ? 'var(--ink-0)' : 'var(--ink-2)',
            border: '1px solid ' + (on ? 'var(--ink-0)' : 'var(--hairline)'),
            borderRadius: 999,
            padding: '4px 12px 4px 4px',
            fontSize: 12,
            cursor:'pointer'
          }},
          React.createElement('span',{style:{
            width:20,height:20,borderRadius:999,
            background: on ? 'var(--paper-0)' : done ? 'var(--accent-wash)' : 'var(--paper-1)',
            color:  on ? 'var(--ink-0)' : done ? 'var(--accent-ink)' : 'var(--ink-2)',
            fontSize:11,fontWeight:600,
            display:'flex',alignItems:'center',justifyContent:'center',
            border:'1px solid ' + (on ? 'var(--paper-0)' : done ? 'var(--accent-wash-2)' : 'var(--hairline)')
          }}, done ? React.createElement(Icon.Check) : (i+1)),
          React.createElement('span',{style:{fontWeight: on ? 600 : 500}}, s.label)
        )
      );
    })
  );
}
window.Stepper = Stepper;

function ServiceHeader({service, step, onStep, steps}){
  return React.createElement('div',{className:'svchead hair-b', style:{padding:'14px 18px', background:'var(--paper-0)'}},
    React.createElement('div',{className:'row gap-3'},
      React.createElement('div',{className:'col gap-1', style:{flex:1, minWidth:0}},
        React.createElement('div',{className:'row gap-2'},
          React.createElement('span',{className:'t-label'}, service.type),
          React.createElement('span',{className:'t-hint'}, '·'),
          React.createElement('span',{className:'t-mono t-hint'}, service.id),
        ),
        React.createElement('div',{className:'row gap-3', style:{marginTop:2}},
          React.createElement('span',{className:'t-display'}, service.display),
          React.createElement(HealthDot,{state:service.health}),
          React.createElement('span',{className:'pill'}, service.revision),
          service.binding==='bound' && React.createElement('span',{className:'pill ok'}, 'serving'),
          service.binding==='draft' && React.createElement('span',{className:'pill warn'}, 'draft'),
          service.binding==='unbound' && React.createElement('span',{className:'pill'}, 'unbound'),
        ),
        service.description && React.createElement('div',{className:'t-mute', style:{marginTop:6, fontSize:12, maxWidth:720}}, service.description)
      ),
      React.createElement('div',{className:'col gap-2', style:{alignItems:'flex-end'}},
        React.createElement('div',{className:'row gap-2'},
          React.createElement('button',{className:'btn'}, 'Compare runs'),
          React.createElement('button',{className:'btn'}, 'Share'),
          React.createElement('button',{className:'btn primary'}, React.createElement(Icon.Play), 'Invoke'),
        ),
        React.createElement('div',{className:'row gap-3', style:{fontSize:11, color:'var(--ink-3)'}},
          React.createElement('span',null, 'owner · ', React.createElement('b',{style:{color:'var(--ink-1)',fontWeight:500}}, service.owner)),
          React.createElement('span',null, 'last run · ', service.lastRun)
        )
      )
    ),
    React.createElement('div',{style:{marginTop:14}},
      React.createElement(Stepper,{steps, active:step, onChange:onStep, service})
    )
  );
}
window.ServiceHeader = ServiceHeader;
