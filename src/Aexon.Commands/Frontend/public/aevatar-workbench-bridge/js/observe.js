// Observe step — run compare, health rail, governance snapshot, human escalation playback

function CompareDiff(){
  const rows = [
    {k:'intent classification', a:'refund (0.94)', b:'refund (0.91)', same:true},
    {k:'risk.sla_breach',       a:'true',          b:'false',          same:false, dir:'new-risk'},
    {k:'refund cap',            a:'$140',          b:'$120',           same:false, dir:'change'},
    {k:'steps',                 a:'4 (1 err, 1 retry)', b:'4 (0 err)',  same:false, dir:'regression'},
    {k:'elapsed',               a:'4.58s',         b:'2.71s',          same:false, dir:'regression'},
    {k:'resolution',            a:'awaiting L2',   b:'auto-reply',     same:false, dir:'hand-off'},
  ];
  return React.createElement('div',{className:'card'},
    React.createElement('div',{className:'card-h'},
      React.createElement('span',{className:'t-title',style:{fontSize:13}}, 'Run compare'),
      React.createElement('span',{className:'pill'}, 'run_9183 vs run_9179 (last success)'),
      React.createElement('div',{className:'grow'}),
      React.createElement('span',{className:'prov live'}, 'baseline · live'),
    ),
    React.createElement('table',{style:{width:'100%', borderCollapse:'collapse', fontSize:12.5}},
      React.createElement('thead',null, React.createElement('tr',{style:{background:'var(--paper-1)'}},
        ['Signal','Current · run_9183','Baseline · run_9179','Δ'].map(h =>
          React.createElement('th',{key:h, style:{textAlign:'left', padding:'8px 12px', fontWeight:500, color:'var(--ink-2)', fontSize:11, textTransform:'uppercase', letterSpacing:'.06em', borderBottom:'1px solid var(--hairline)'}}, h))
      )),
      React.createElement('tbody',null,
        rows.map(r => React.createElement('tr',{key:r.k, style:{borderBottom:'1px solid var(--hairline-soft)'}},
          React.createElement('td',{style:{padding:'10px 12px', color:'var(--ink-1)'}}, r.k),
          React.createElement('td',{className:'t-mono', style:{padding:'10px 12px'}}, r.a),
          React.createElement('td',{className:'t-mono', style:{padding:'10px 12px', color:'var(--ink-2)'}}, r.b),
          React.createElement('td',{style:{padding:'10px 12px'}},
            r.same ? React.createElement('span',{className:'pill',style:{padding:'0 6px',fontSize:10}}, 'same')
              : r.dir==='new-risk' ? React.createElement('span',{className:'pill warn'},'new risk')
              : r.dir==='regression' ? React.createElement('span',{className:'pill err'},'regression')
              : r.dir==='hand-off' ? React.createElement('span',{className:'pill accent'},'hand-off')
              : React.createElement('span',{className:'pill copper'},'changed'))
        )))
    )
  );
}

function HealthRail(){
  const items = [
    {k:'runtime', v:'degraded', tone:'warn', note:'1 step error in last 10 runs'},
    {k:'bindings', v:'serving · 2', tone:'ok', note:'prod + staging · canary paused'},
    {k:'human override', v:'1 active', tone:'copper', note:'run_9183 awaiting L2'},
    {k:'risky to change', v:'yes', tone:'warn', note:'baseline exists — prior good state r-0f2a'},
    {k:'observability', v:'partial', tone:'mute', note:'audit stream delayed 4s', prov:'partial'},
  ];
  return React.createElement('div',{className:'card'},
    React.createElement('div',{className:'card-h'},
      React.createElement('span',{className:'t-title',style:{fontSize:13}}, 'Health & trust'),
      React.createElement('div',{className:'grow'}),
      React.createElement('span',{className:'prov partial'}, '3/5 live · 1 partial · 1 delayed')),
    React.createElement('div',{className:'card-b col gap-2'},
      items.map((it,i) => React.createElement('div',{key:i, className:'row gap-3', style:{padding:'6px 0', borderBottom: i<items.length-1?'1px solid var(--hairline-soft)':'none'}},
        React.createElement('div',{className:'t-label', style:{width:130}}, it.k),
        React.createElement('div',{className:'col', style:{flex:1}},
          React.createElement('div',null,
            React.createElement('span',{className:`pill ${it.tone}`}, it.v)),
          React.createElement('div',{className:'t-hint', style:{fontSize:11, marginTop:3}}, it.note)
        )
      )))
  );
}

function GovernanceSnapshot(){
  const rows = [
    ['currently serving', 'r-0f2a · v3', 'prod + staging'],
    ['last change',       '2h ago', 'risk script · refund cap 120→140'],
    ['auditability',      'live', 'runs + audit + AGUI stream'],
    ['known fallback',    'r-0d11 · v2', 'can roll back in 45s'],
    ['owner',             'runtime-ops', 'approved by @nadia.liao'],
  ];
  return React.createElement('div',{className:'card'},
    React.createElement('div',{className:'card-h'},
      React.createElement('span',{className:'t-title',style:{fontSize:13}}, 'Governance snapshot'),
      React.createElement('div',{className:'grow'}),
      React.createElement('button',{className:'btn small'}, 'Roll back to r-0d11')),
    React.createElement('div',{className:'card-b'},
      React.createElement('div',{style:{display:'grid', gridTemplateColumns:'140px 160px 1fr', rowGap:8, columnGap:16, fontSize:12.5}},
        rows.flatMap(([k,v,n],i) => [
          React.createElement('div',{key:'k'+i, className:'t-label'}, k),
          React.createElement('div',{key:'v'+i, style:{fontWeight:500, color:'var(--ink-0)'}}, v),
          React.createElement('div',{key:'n'+i, className:'t-mute'}, n)
        ])))
  );
}

function HumanEscalationPlayback(){
  const frames = [
    {t:'4.58s', who:'Escalation Decider', msg:'refund $140 exceeds auto-threshold → request L2 approval', tone:'warn'},
    {t:'4.58s', who:'system', msg:'run paused · waiting on approval request apr_0a91', tone:'mute'},
    {t:'42m',   who:'jade.yu (L2)', msg:'approved · note: customer is repeat contact, wave fee', tone:'ok'},
    {t:'42m',   who:'system', msg:'run resumed · draft updated · sent via Telegram', tone:'ok'},
  ];
  return React.createElement('div',{className:'card'},
    React.createElement('div',{className:'card-h'},
      React.createElement('span',{className:'t-title',style:{fontSize:13}}, 'Human escalation · playback'),
      React.createElement('div',{className:'grow'}),
      React.createElement('span',{className:'pill copper'}, '1 hand-off · 41m 12s out'),
    ),
    React.createElement('div',{className:'card-b col gap-2'},
      frames.map((f,i)=>React.createElement('div',{key:i, className:'row gap-3', style:{padding:'8px 0', borderBottom: i<frames.length-1?'1px solid var(--hairline-soft)':'none'}},
        React.createElement('div',{className:'t-mono',style:{width:60, color:'var(--ink-3)', fontSize:11}}, f.t),
        React.createElement('div',{style:{width:160, fontWeight:500, color:'var(--ink-0)', fontSize:12.5}}, f.who),
        React.createElement('div',{style:{flex:1, color:'var(--ink-1)', fontSize:12.5}}, f.msg),
        React.createElement('span',{className:`pill ${f.tone}`, style:{padding:'0 6px',fontSize:10}}, f.tone==='ok'?'resolved':f.tone==='warn'?'hand-off':'paused')
      )))
  );
}

function ObserveStep({service}){
  return React.createElement('div',{style:{padding:16, display:'grid', gap:14, gridTemplateColumns:'1fr 360px'}},
    React.createElement('div',{className:'col gap-3'},
      React.createElement(CompareDiff),
      React.createElement(HumanEscalationPlayback),
      React.createElement(GovernanceSnapshot),
    ),
    React.createElement('div',{className:'col gap-3'},
      React.createElement(HealthRail),
      React.createElement('div',{className:'card'},
        React.createElement('div',{className:'card-h'},
          React.createElement('span',{className:'t-title',style:{fontSize:13}}, 'Unavailable right now'),
          React.createElement('div',{className:'grow'}),
          React.createElement('span',{className:'prov unavail'}, 'degraded')),
        React.createElement('div',{className:'card-b col gap-2', style:{fontSize:12}},
          React.createElement('div',null,
            React.createElement('span',{className:'pill err'}, 'topology'),
            ' Actor graph read failed · ',
            React.createElement('a',{href:'#', style:{color:'var(--accent)'}}, 'retry')),
          React.createElement('div',{className:'t-hint',style:{fontSize:11}}, 'We show ',
            React.createElement('b',null,'member-level'), ' edges seeded from the workflow DAG. Not the full runtime topology.'),
          React.createElement('div',{style:{marginTop:6}},
            React.createElement('span',{className:'pill warn'}, 'delayed · audit'),
            ' Audit stream is ~4s behind · ',
            React.createElement('a',{href:'#',style:{color:'var(--accent)'}}, 'more'))
        ))
    )
  );
}
window.ObserveStep = ObserveStep;
