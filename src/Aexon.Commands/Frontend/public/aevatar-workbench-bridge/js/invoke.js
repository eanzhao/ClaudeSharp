// Invoke step — playground + integrated AGUI event panel (timeline / trace / tabs modes)

function kindMeta(k){
  const m = {
    'run.start':    { label:'run start',      color:'var(--accent)',  group:'meta'  },
    'step.start':   { label:'step start',     color:'var(--accent)',  group:'step'  },
    'step.done':    { label:'step done',      color:'var(--ok)',      group:'step'  },
    'step.error':   { label:'step error',     color:'var(--err)',     group:'step'  },
    'tool.call':    { label:'tool call',      color:'var(--copper)',  group:'tool'  },
    'tool.result':  { label:'tool result',    color:'var(--copper)',  group:'tool'  },
    'thinking':     { label:'thinking',       color:'var(--ink-2)',   group:'msg'   },
    'human.request':{ label:'human request',  color:'var(--warn)',    group:'hil'   },
    'handoff':      { label:'hand-off',       color:'var(--accent)',  group:'meta'  },
    'ctx.change':   { label:'context change', color:'var(--mute)',    group:'meta'  },
    'retry':        { label:'retry',          color:'var(--warn)',    group:'meta'  },
  };
  return m[k] || {label:k, color:'var(--ink-3)', group:'meta'};
}

// --- streaming simulation ---
function useEventStream(running){
  const [emitted, setEmitted] = useState(window.DEMO.runEvents.slice(0, 3));
  const [complete, setComplete] = useState(false);
  const timer = useRef(null);
  useEffect(() => {
    if(!running){ return; }
    setEmitted([]);setComplete(false);
    let i = 0;
    const tick = () => {
      i++;
      setEmitted(window.DEMO.runEvents.slice(0, i));
      if(i >= window.DEMO.runEvents.length){
        setComplete(true); clearInterval(timer.current);
      }
    };
    timer.current = setInterval(tick, 320);
    return () => clearInterval(timer.current);
  }, [running]);
  return {events: emitted, complete, reset: () => { setEmitted(window.DEMO.runEvents); setComplete(true); }};
}

// --- Metrics bar ---
function MetricsBar({events, running}){
  const steps = events.filter(e => e.kind.startsWith('step.')).length;
  const tools = events.filter(e => e.kind.startsWith('tool.')).length;
  const errs = events.filter(e => e.kind==='step.error').length;
  const last = events[events.length-1];
  const elapsed = last ? formatMs(last.t) : '—';
  const Metric = ({label, value, accent}) => React.createElement('div',{className:'col', style:{padding:'4px 12px', borderRight:'1px solid var(--hairline)', minWidth:80}},
    React.createElement('span',{className:'t-label', style:{fontSize:10}}, label),
    React.createElement('span',{style:{fontVariantNumeric:'tabular-nums', fontSize:16, fontWeight:600, color:accent||'var(--ink-0)'}}, value));
  return React.createElement('div',{style:{display:'flex', alignItems:'stretch', border:'1px solid var(--hairline)', borderRadius:8, background:'var(--paper-0)'}},
    React.createElement(Metric,{label:'events', value:events.length}),
    React.createElement(Metric,{label:'steps', value:steps}),
    React.createElement(Metric,{label:'tool calls', value:tools}),
    React.createElement(Metric,{label:'errors', value:errs, accent: errs?'var(--err)':'var(--ink-0)'}),
    React.createElement(Metric,{label:'elapsed', value:elapsed}),
    React.createElement('div',{className:'col', style:{padding:'4px 12px', flex:1, justifyContent:'center'}},
      React.createElement('span',{className:'t-label', style:{fontSize:10}}, 'state'),
      React.createElement('div',{className:'row gap-2', style:{marginTop:4}},
        running
          ? React.createElement('span',{className:'pill accent'},
              React.createElement('span',{style:{width:8,height:8,borderRadius:999,background:'var(--accent)',animation:'pulse 1.2s infinite'}}), 'streaming')
          : (errs>0 ? React.createElement('span',{className:'pill err'},'error')
                    : React.createElement('span',{className:'pill ok'},'ready')),
        React.createElement('span',{className:'prov live'},'sse · live')))
  );
}

// --- Timeline mode ---
function TimelineView({events, focused, onFocus}){
  return React.createElement('div',{style:{position:'relative'}},
    React.createElement('div',{style:{position:'absolute', left:96, top:0, bottom:0, width:1, background:'var(--hairline)'}}),
    events.map((e, i) => {
      const m = kindMeta(e.kind);
      const active = i===focused;
      return React.createElement('div',{key:i, onClick:()=>onFocus(i),
        style:{position:'relative', display:'grid', gridTemplateColumns:'88px 1fr', gap:16, padding:'6px 12px', cursor:'pointer', background: active?'var(--accent-wash)':'transparent', borderRadius:6}},
        React.createElement('div',{className:'t-mono', style:{fontSize:11, color:'var(--ink-3)', textAlign:'right', paddingTop:7, fontVariantNumeric:'tabular-nums'}}, formatMs(e.t)),
        React.createElement('div',{style:{paddingLeft:18, position:'relative'}},
          React.createElement('span',{style:{position:'absolute', left:-4, top:10, width:10, height:10, borderRadius:999, background:m.color, border:'2px solid var(--paper-0)'}}),
          React.createElement('div',{className:'row gap-2'},
            React.createElement('span',{className:'pill', style:{background:'transparent',borderColor:m.color+'33',color:m.color,padding:'0 6px',fontSize:10}}, m.label),
            React.createElement('span',{style:{fontWeight:500,color:'var(--ink-0)',fontSize:13}}, e.label),
            e.step && React.createElement('span',{className:'t-hint',style:{fontSize:11}}, '· ', e.step)
          ),
          e.detail && React.createElement('div',{className:'t-mute',style:{fontSize:12,marginTop:2}}, e.detail),
          active && e.thinking && React.createElement('div',{style:{marginTop:6,padding:'8px 10px',background:'var(--paper-1)',borderRadius:6,fontSize:12,color:'var(--ink-2)',fontStyle:'italic',borderLeft:'2px solid var(--ink-3)'}}, e.thinking),
          active && e.args && React.createElement('pre',{className:'t-mono',style:{margin:'6px 0 0',padding:8,background:'var(--paper-1)',borderRadius:6,fontSize:11.5,border:'1px solid var(--hairline)'}}, 'args = ', JSON.stringify(e.args,null,2)),
          active && e.result && React.createElement('pre',{className:'t-mono',style:{margin:'6px 0 0',padding:8,background:'var(--paper-1)',borderRadius:6,fontSize:11.5,border:'1px solid var(--hairline)'}}, 'result = ', JSON.stringify(e.result,null,2)),
          active && e.error && React.createElement('pre',{className:'t-mono',style:{margin:'6px 0 0',padding:8,background:'var(--err-wash)',color:'var(--err)',borderRadius:6,fontSize:11.5,border:'1px solid var(--err-wash)'}}, e.error),
        )
      );
    })
  );
}

// --- Trace / waterfall mode ---
function TraceView({events}){
  const stepRows = {};
  events.forEach(e => {
    if(!e.step) return;
    const keys = e.step.split(/[,→]/).map(s=>s.trim()).filter(Boolean);
    keys.forEach(k => {
      if(!stepRows[k]) stepRows[k] = {start: e.t, end: e.t, errors:0, tools:0};
      stepRows[k].end = Math.max(stepRows[k].end, e.t);
      if(e.kind==='step.error') stepRows[k].errors++;
      if(e.kind==='tool.call') stepRows[k].tools++;
    });
  });
  const max = Math.max(1, ...Object.values(stepRows).map(r => r.end));
  const rows = Object.entries(stepRows);
  return React.createElement('div',{style:{padding:'12px 8px'}},
    rows.map(([name, r]) => {
      const left = (r.start/max)*100;
      const width = Math.max(1.2, ((r.end - r.start)/max)*100);
      const err = r.errors>0;
      return React.createElement('div',{key:name, style:{display:'grid', gridTemplateColumns:'130px 1fr 90px', gap:12, alignItems:'center', padding:'6px 0'}},
        React.createElement('div',{style:{fontSize:12, color:'var(--ink-1)', fontWeight:500}}, name),
        React.createElement('div',{style:{position:'relative', height:18, background:'var(--paper-1)', borderRadius:4, border:'1px solid var(--hairline)'}},
          React.createElement('div',{style:{position:'absolute', left:left+'%', width:width+'%', top:0, bottom:0, background: err?'var(--err-wash)':'var(--accent-wash)', borderRadius:3, border:'1px solid ' + (err?'var(--err)':'var(--accent-wash-2)')}}),
          r.tools>0 && React.createElement('span',{style:{position:'absolute', left:(left+width/2)+'%', top:3, transform:'translateX(-50%)', fontSize:10, color:'var(--copper-ink)', fontFamily:'var(--font-mono)'}}, r.tools+' tool'+(r.tools>1?'s':''))
        ),
        React.createElement('div',{className:'t-mono',style:{fontSize:11,color:'var(--ink-2)',textAlign:'right'}}, formatMs(r.end - r.start))
      );
    }),
    rows.length===0 && React.createElement('div',{className:'t-hint', style:{padding:'30px 14px', textAlign:'center'}}, 'Waiting for first step start…')
  );
}

// --- Tabs mode (Steps / Tool Calls / Thinking / Messages) ---
function TabsView({events}){
  const [tab, setTab] = useState('steps');
  const items = {
    steps: events.filter(e => e.kind.startsWith('step.') || e.kind==='handoff' || e.kind==='retry'),
    tools: events.filter(e => e.kind.startsWith('tool.')),
    think: events.filter(e => e.kind==='thinking'),
    msg:   events.filter(e => e.kind==='human.request' || e.kind==='ctx.change' || e.kind==='run.start'),
  };
  return React.createElement('div',{className:'col', style:{minHeight:0}},
    React.createElement('div',{className:'tabs', style:{padding:'0 6px'}},
      [['steps','Steps',items.steps.length],['tools','Tool calls',items.tools.length],['think','Thinking',items.think.length],['msg','Messages',items.msg.length]]
        .map(([k,l,n]) => React.createElement('button',{key:k, className:'tab ' + (tab===k?'active':''), onClick:()=>setTab(k)}, l, ' ', React.createElement('span',{className:'pill',style:{marginLeft:6,fontSize:10,padding:'0 5px'}},n)))
    ),
    React.createElement('div',{style:{padding:'10px 8px', overflow:'auto', flex:1}},
      items[tab].map((e,i) => {
        const m = kindMeta(e.kind);
        return React.createElement('div',{key:i, className:'hair', style:{padding:'10px 12px', borderRadius:6, marginBottom:6, background:'var(--paper-0)'}},
          React.createElement('div',{className:'row gap-2'},
            React.createElement('span',{className:'pill', style:{background:'transparent',borderColor:m.color+'33',color:m.color,padding:'0 6px',fontSize:10}}, m.label),
            React.createElement('span',{style:{fontWeight:500,fontSize:12.5}}, e.label),
            React.createElement('div',{className:'grow'}),
            React.createElement('span',{className:'t-mono t-hint',style:{fontSize:11}}, formatMs(e.t))),
          e.detail && React.createElement('div',{className:'t-mute',style:{fontSize:12,marginTop:4}}, e.detail),
          e.thinking && React.createElement('div',{style:{marginTop:6,padding:'6px 10px',background:'var(--paper-1)',borderRadius:4,fontSize:12,color:'var(--ink-2)',fontStyle:'italic'}}, e.thinking),
          e.args && React.createElement('pre',{className:'t-mono',style:{margin:'6px 0 0',padding:8,background:'var(--paper-1)',borderRadius:4,fontSize:11.5}}, JSON.stringify(e.args,null,2))
        );
      }),
      items[tab].length===0 && React.createElement('div',{className:'t-hint',style:{textAlign:'center',padding:'20px'}}, 'No ',tab,' yet')
    )
  );
}

// --- Bubbles mode (assistant + tool messages) ---
function BubblesView({events}){
  const frames = [
    {role:'user', text:'refund for order #92817 — 3rd time asking', meta:'telegram · alex · 0s'},
    {role:'thinking', text:events.find(e=>e.kind==='thinking')?.thinking, meta:'intake · 0.38s'},
    {role:'tool', text:'classify_intent({text:…, locale:"en"})  ⇢  {intent:"refund", priority:"high"}', meta:'tool · 0.72s'},
    {role:'assistant', text:'Identified as refund request, high priority. Routing to knowledge and risk review in parallel.', meta:'intake · 1.24s'},
    {role:'tool', text:'kb_search({query:"refund failed payment 92817"})  ⇢  3 docs', meta:'tool · 2.05s'},
    {role:'system', text:'risk script threw TypeError; retrying…', meta:'risk · 2.98s', tone:'warn'},
    {role:'assistant', text:'Drafted reply with KB-441 grounding. Sending to escalation decider.', meta:'knowledge · 4.12s'},
    {role:'hil', text:'Escalation decider needs L2 approval to refund $140. Waiting.', meta:'escalate · 4.58s'},
  ];
  const bg = r => ({user:'var(--paper-1)', assistant:'var(--paper-0)', tool:'var(--copper-wash)', thinking:'var(--paper-1)', hil:'var(--warn-wash)', system:'var(--paper-1)'})[r] || 'var(--paper-1)';
  const align = r => r==='user' ? 'flex-end' : 'flex-start';
  return React.createElement('div',{style:{padding:'10px 16px'}},
    frames.slice(0, Math.min(frames.length, Math.max(1, Math.floor(events.length/2)))).map((f,i) =>
      React.createElement('div',{key:i, style:{display:'flex', justifyContent:align(f.role), marginBottom:8}},
        React.createElement('div',{style:{maxWidth:520, padding:'8px 12px', background:bg(f.role), border:'1px solid ' + (f.tone==='warn'?'var(--warn-wash)':'var(--hairline)'), borderRadius:10}},
          React.createElement('div',{className:'row gap-2',style:{marginBottom:3}},
            React.createElement('span',{className:'t-label',style:{fontSize:9.5,color:f.role==='assistant'?'var(--accent)':f.role==='tool'?'var(--copper)':'var(--ink-2)'}}, f.role),
            React.createElement('span',{className:'t-hint',style:{fontSize:10}}, f.meta)),
          React.createElement('div',{style:{fontSize:13, color:'var(--ink-0)', fontStyle: f.role==='thinking'?'italic':'normal'}}, f.text)))
    )
  );
}

// --- Raw SSE view ---
function RawView({events}){
  return React.createElement('pre',{className:'t-mono', style:{margin:0, padding:'10px 14px', fontSize:11.5, overflow:'auto', color:'var(--ink-1)', lineHeight:1.55}},
    events.map((e,i) =>
`event: ${e.kind}
data: ${JSON.stringify({t:e.t, label:e.label, step:e.step, detail:e.detail, ...(e.args?{args:e.args}:{}), ...(e.result?{result:e.result}:{}) })}

`).join(''));
}

// --- AGUI panel composer ---
function AguiPanel({events, mode, running, onModeChange, focused, onFocus}){
  return React.createElement('div',{className:'card', style:{display:'flex', flexDirection:'column', minHeight:0}},
    React.createElement('div',{className:'card-h'},
      React.createElement('span',{className:'t-title', style:{fontSize:13}}, 'AGUI events'),
      React.createElement('span',{className:'pill accent'}, events.length, ' frames'),
      React.createElement('span',{className:running?'prov live':'prov delayed'}, running?'sse · live':'sse · idle'),
      React.createElement('div',{className:'grow'}),
      React.createElement('div',{className:'row gap-1'},
        ['timeline','trace','tabs','bubbles','raw'].map(m =>
          React.createElement('button',{key:m, className:'btn small', onClick:()=>onModeChange(m), style:{background: mode===m?'var(--ink-0)':'transparent', color: mode===m?'var(--paper-0)':'var(--ink-1)', borderColor: mode===m?'var(--ink-0)':'var(--hairline)'}}, m))
      )
    ),
    React.createElement('div',{style:{padding:'10px 12px', borderBottom:'1px solid var(--hairline)'}},
      React.createElement(MetricsBar,{events, running})
    ),
    React.createElement('div',{style:{flex:1, overflow:'auto', padding: mode==='raw'?0:'8px 8px 14px'}},
      mode==='timeline' && React.createElement(TimelineView,{events, focused, onFocus}),
      mode==='trace'    && React.createElement(TraceView,{events}),
      mode==='tabs'     && React.createElement(TabsView,{events}),
      mode==='bubbles'  && React.createElement(BubblesView,{events}),
      mode==='raw'      && React.createElement(RawView,{events}),
    )
  );
}

// --- Human-in-the-loop inline prompt ---
function HILPrompt({event, onResolve}){
  if(!event) return null;
  return React.createElement('div',{style:{border:'1px solid var(--warn-wash)', background:'var(--warn-wash)', borderRadius:8, padding:'12px 14px'}},
    React.createElement('div',{className:'row gap-2'},
      React.createElement('span',{className:'pill warn'}, 'human · approval'),
      React.createElement('span',{className:'t-hint', style:{fontSize:11}}, 'request ', React.createElement('span',{className:'t-mono'},event.requestId), ' · awaiting L2')
    ),
    React.createElement('div',{style:{fontSize:13, color:'var(--ink-0)', marginTop:6, fontWeight:500}}, event.prompt),
    React.createElement('div',{className:'row gap-2', style:{marginTop:10}},
      React.createElement('button',{className:'btn primary', onClick:()=>onResolve('approve')}, React.createElement(Icon.Check), 'Approve refund'),
      React.createElement('button',{className:'btn'}, 'Edit amount'),
      React.createElement('button',{className:'btn ghost', onClick:()=>onResolve('deny')}, 'Reject'),
      React.createElement('div',{className:'grow'}),
      React.createElement('span',{className:'t-hint',style:{fontSize:11}}, 'run resumes on answer')
    )
  );
}

// --- History rail ---
function HistoryRail({active, onPick}){
  return React.createElement('div',{className:'col gap-1'},
    window.DEMO.history.map(h => {
      const on = h.id===active;
      return React.createElement('button',{key:h.id, onClick:()=>onPick(h.id),
        style:{display:'flex', alignItems:'flex-start', gap:8, padding:'8px 10px', borderRadius:6,
          background: on ? 'var(--accent-wash)':'transparent',
          border:'1px solid ' + (on ? 'var(--accent-wash-2)':'transparent'),
          cursor:'pointer', textAlign:'left', width:'100%'}},
        React.createElement('span',{style:{marginTop:4, width:7, height:7, borderRadius:2, background: h.status==='ok'?'var(--ok)':h.status==='error'?'var(--err)':'var(--warn)'}}),
        React.createElement('div',{className:'col', style:{flex:1, minWidth:0}},
          React.createElement('div',{className:'row gap-2'},
            React.createElement('span',{className:'t-mono',style:{fontSize:11.5, color:'var(--ink-0)'}}, h.id),
            React.createElement('div',{className:'grow'}),
            React.createElement('span',{className:'t-hint',style:{fontSize:10.5}}, h.when)),
          React.createElement('div',{style:{fontSize:12, color:'var(--ink-1)', overflow:'hidden', textOverflow:'ellipsis', whiteSpace:'nowrap', marginTop:2}}, h.input.text),
          React.createElement('div',{className:'t-hint',style:{fontSize:11, marginTop:2}}, h.note)
        ));
    })
  );
}

function PlaygroundForm({onRun, running}){
  const [body, setBody] = useState('{\n  "channel": "telegram",\n  "text": "refund for order #92817 — 3rd time asking",\n  "user": "alex"\n}');
  return React.createElement('div',{className:'card'},
    React.createElement('div',{className:'card-h'},
      React.createElement('span',{className:'t-title',style:{fontSize:13}}, 'Playground'),
      React.createElement('div',{className:'grow'}),
      React.createElement('span',{className:'pill'}, 'POST /svc/sc-38f2/support-triage'),
    ),
    React.createElement('div',{className:'card-b'},
      React.createElement('div',{className:'row gap-2', style:{flexWrap:'wrap'}},
        React.createElement('span',{className:'pill'}, 'auth · nyxid_7c2f9…a01b'),
        React.createElement('span',{className:'pill'}, 'stream · SSE'),
        React.createElement('span',{className:'pill'}, 'accept · AGUI frames'),
        React.createElement('div',{className:'grow'}),
        React.createElement('button',{className:'btn small'}, 'Load fixture'),
        React.createElement('button',{className:'btn small'}, 'Save request')),
      React.createElement('textarea',{className:'input', rows:5, value:body, onChange:e=>setBody(e.target.value), style:{width:'100%', marginTop:8, fontSize:12}}),
      React.createElement('div',{className:'row gap-2', style:{marginTop:10}},
        React.createElement('button',{className:`btn ${running?'':'primary'}`, onClick:onRun, disabled:running},
          React.createElement(Icon.Play), running?'streaming…':'Run'),
        React.createElement('button',{className:'btn'}, 'Replay last'),
        React.createElement('div',{className:'grow'}),
        React.createElement('span',{className:'t-hint'}, 'Streams AGUI frames into the panel →'))
    )
  );
}

function InvokeStep({service}){
  const [aguiMode, setAguiMode] = useState(window.TWEAKS?.aguiMode || 'timeline');
  const [layout, setLayout] = useState(window.TWEAKS?.layout || 'split');
  const [running, setRunning] = useState(false);
  const [runId, setRunId] = useState('run_9183');
  const [focused, setFocused] = useState(-1);
  const stream = useEventStream(running);
  const [resolved, setResolved] = useState(null);

  // subscribe to tweaks
  useEffect(() => {
    const h = (e) => {
      if(e.detail?.aguiMode) setAguiMode(e.detail.aguiMode);
      if(e.detail?.layout) setLayout(e.detail.layout);
    };
    window.addEventListener('tweaks-change', h);
    return () => window.removeEventListener('tweaks-change', h);
  }, []);

  const events = running ? stream.events : window.DEMO.runEvents.slice(0, 17);
  const hil = events.find(e => e.kind==='human.request');

  const onRun = () => { setRunning(true); setResolved(null);
    setTimeout(()=>setRunning(false), 320*(window.DEMO.runEvents.length+1)); };

  const playground = React.createElement(React.Fragment, null,
    React.createElement(PlaygroundForm,{onRun, running}),
    hil && !resolved && React.createElement('div',{style:{marginTop:12}},
      React.createElement(HILPrompt,{event:hil, onResolve:setResolved})),
    resolved && React.createElement('div',{style:{marginTop:12, padding:'10px 12px', background:'var(--ok-wash)', color:'var(--ok)', borderRadius:6, fontSize:12}},
      React.createElement(Icon.Check), ' Resolved · ', resolved==='approve'?'approved refund $140. Run resumed.':'rejected. Run escalated to supervisor.')
  );

  const aguiBlock = React.createElement(AguiPanel, {
    events, mode:aguiMode, running, onModeChange:setAguiMode, focused, onFocus:setFocused
  });

  // layout: split (left 420 / right), stack (top / bottom), canvas (right rail + bottom)
  const gridStyle = layout==='stack'
    ? {gridTemplateColumns:'1fr', gridTemplateRows:'auto 1fr'}
    : layout==='canvas'
    ? {gridTemplateColumns:'260px 1fr 420px'}
    : {gridTemplateColumns:'420px 1fr'};

  const historyRail = React.createElement('div',{className:'card', style:{overflow:'hidden', display:'flex', flexDirection:'column', minHeight:0}},
    React.createElement('div',{className:'card-h'},
      React.createElement('span',{className:'t-title', style:{fontSize:13}}, 'Request history'),
      React.createElement('div',{className:'grow'}),
      React.createElement('span',{className:'prov live'}, 'audit · live')),
    React.createElement('div',{style:{padding:'8px 8px', overflow:'auto'}},
      React.createElement(HistoryRail,{active:runId, onPick:setRunId}))
  );

  return React.createElement('div',{style:{padding:16, display:'grid', gap:14, minHeight:0, flex:1, ...gridStyle}},
    layout==='canvas' && historyRail,
    React.createElement('div',{className:'col gap-3', style:{minHeight:0}},
      playground,
      layout==='stack' && aguiBlock,
      layout!=='stack' && layout!=='canvas' && historyRail,
    ),
    layout!=='stack' && aguiBlock
  );
}
window.InvokeStep = InvokeStep;
