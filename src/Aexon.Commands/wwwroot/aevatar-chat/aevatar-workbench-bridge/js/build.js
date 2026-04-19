// Build step — Workflow DAG canvas, Script editor, GAgent form; with type selector
// Exports: BuildStep component

function BuildTypeSelector({type, onChange}){
  const opts = [
    {k:'workflow', label:'Workflow', icon:Icon.Workflow, desc:'Compose steps as a DAG. Best when the flow is known and parallel fan-out matters.', when:'Multiple agents hand off predictably'},
    {k:'script',   label:'Script',   icon:Icon.Script,   desc:'Write a typed script (TS) that calls tools, loops, branches. Best for deterministic business logic.', when:'You need code-level control'},
    {k:'gagent',   label:'GAgent',   icon:Icon.GAgent,   desc:'Wire a typed GAgent grain with Orleans state. Best when a single actor owns long-lived state.', when:'State lives with one agent'},
  ];
  return React.createElement('div', {className:'col gap-2'},
    React.createElement('span',{className:'t-label'}, 'Construction mode'),
    React.createElement('div',{style:{display:'grid', gridTemplateColumns:'1fr 1fr 1fr', gap:10}},
      opts.map(o => {
        const on = type === o.k;
        return React.createElement('button',{
          key:o.k, onClick:()=>onChange(o.k),
          style:{
            textAlign:'left', cursor:'pointer',
            background: on ? 'var(--accent-wash)' : 'var(--paper-0)',
            border: '1px solid ' + (on ? 'var(--accent)' : 'var(--hairline)'),
            borderRadius:10, padding:12, color:'var(--ink-1)'
          }},
          React.createElement('div',{className:'row gap-2'},
            React.createElement('span',{style:{color: on ? 'var(--accent-ink)' : 'var(--ink-2)'}}, React.createElement(o.icon)),
            React.createElement('span',{className:'t-title', style:{fontSize:14}}, o.label),
            on && React.createElement('span',{className:'pill accent', style:{marginLeft:'auto', padding:'0 6px',fontSize:10}}, 'selected')
          ),
          React.createElement('div',{className:'t-mute', style:{fontSize:12, marginTop:6, lineHeight:1.45}}, o.desc),
          React.createElement('div',{className:'t-hint', style:{marginTop:8, fontSize:11}}, 'When · ', o.when)
        );
      })
    )
  );
}

// --- Workflow DAG canvas ---
function WorkflowCanvas({dag, onSelectNode, selected}){
  const W = 900, H = 320;
  const nodeOf = (id) => dag.nodes.find(n => n.id===id);
  return React.createElement('div',{style:{background:'var(--paper-0)', border:'1px solid var(--hairline)', borderRadius:10, position:'relative', overflow:'hidden'}},
    // gridded bg
    React.createElement('div',{style:{
      position:'absolute', inset:0,
      backgroundImage: 'radial-gradient(var(--paper-3) 1px, transparent 1px)',
      backgroundSize: '16px 16px', opacity:0.6
    }}),
    React.createElement('svg',{viewBox:`0 0 ${W} ${H}`, width:'100%', height:H, style:{display:'block', position:'relative'}},
      // edges
      React.createElement('defs',null,
        React.createElement('marker',{id:'arrow', viewBox:'0 0 10 10', refX:8, refY:5, markerWidth:6, markerHeight:6, orient:'auto'},
          React.createElement('path',{d:'M0,0 L10,5 L0,10 z', fill:'var(--ink-3)'}))
      ),
      dag.edges.map((e,i) => {
        const a = nodeOf(e.from), b = nodeOf(e.to);
        if(!a||!b) return null;
        const ax = a.x+120, ay = a.y+24;
        const bx = b.x,     by = b.y+24;
        const mx = (ax+bx)/2;
        const d = `M ${ax} ${ay} C ${mx} ${ay}, ${mx} ${by}, ${bx} ${by}`;
        return React.createElement('g', {key:i},
          React.createElement('path',{d, fill:'none', stroke:'var(--ink-3)', strokeWidth:1.25, markerEnd:'url(#arrow)'}),
          e.label && React.createElement('text',{x:mx, y:(ay+by)/2 -4, textAnchor:'middle', fontSize:10, fill:'var(--ink-3)', fontFamily:'var(--font-mono)'}, e.label)
        );
      })
    ),
    // nodes overlaid (absolutely positioned)
    React.createElement('div',{style:{position:'absolute', inset:0}},
      dag.nodes.map(n => {
        const active = n.id === selected;
        const color = n.kind==='external' ? 'var(--copper)' : 'var(--accent)';
        const bg    = n.kind==='external' ? 'var(--copper-wash)' : 'var(--accent-wash)';
        return React.createElement('div',{
          key:n.id, onClick:()=>onSelectNode(n.id),
          style:{
            position:'absolute', left:n.x, top:n.y, width:120,
            background: active ? bg : 'var(--paper-0)',
            border: '1px solid ' + (active ? color : 'var(--hairline)'),
            borderLeft: '3px solid ' + color,
            borderRadius:8, padding:'8px 10px',
            cursor:'pointer', boxShadow: active ? '0 2px 8px rgba(0,0,0,0.06)' : 'none'
          }},
          React.createElement('div',{className:'t-label', style:{fontSize:9.5,color:color}}, n.kind),
          React.createElement('div',{style:{fontWeight:600, color:'var(--ink-0)', fontSize:13, marginTop:2}}, n.label),
          React.createElement('div',{className:'t-hint', style:{fontSize:10.5, marginTop:3, lineHeight:1.3}}, n.role),
        );
      })
    ),
    // floating toolbar
    React.createElement('div',{style:{position:'absolute', top:10, right:10, display:'flex', gap:6}},
      React.createElement('button',{className:'btn small'}, React.createElement(Icon.Plus), 'Add step'),
      React.createElement('button',{className:'btn small'}, 'Auto-layout'),
      React.createElement('button',{className:'btn small'}, 'YAML')
    ),
    // legend
    React.createElement('div',{style:{position:'absolute', bottom:10, left:14, display:'flex', gap:14, fontSize:11, color:'var(--ink-3)'}},
      React.createElement('span',null, React.createElement('span',{style:{display:'inline-block',width:10,height:3,background:'var(--accent)',verticalAlign:'middle',marginRight:5}}), 'member'),
      React.createElement('span',null, React.createElement('span',{style:{display:'inline-block',width:10,height:3,background:'var(--copper)',verticalAlign:'middle',marginRight:5}}), 'external'),
    )
  );
}

// --- Script editor (Monaco-like mock) ---
function ScriptEditor(){
  const lines = [
    {n:1,  t:'// risk-review — script construction · TS', c:'c'},
    {n:2,  t:'import { tools, ctx } from "@aevatar/sdk";', c:'k'},
    {n:3,  t:''},
    {n:4,  t:'export async function handle(input: TriageCtx) {', c:'f'},
    {n:5,  t:'  const order = await tools.orderLookup(input.orderId);'},
    {n:6,  t:'  const policy = await tools.kb.refundPolicy(order.region);'},
    {n:7,  t:''},
    {n:8,  t:'  ctx.set("risk.sla_breach", input.age_h > 24);'},
    {n:9,  t:'  const cap = policy.refundLimit(order.total);   // ← bug: order may be null', bug:true},
    {n:10, t:'  if (!policy.allowed) return { ok: false, reason: "policy-block" };'},
    {n:11, t:''},
    {n:12, t:'  return { ok: true, refund_ok: true, cap };'},
    {n:13, t:'}', c:'f'},
  ];
  return React.createElement('div',{className:'col', style:{background:'var(--paper-0)', border:'1px solid var(--hairline)', borderRadius:10, overflow:'hidden'}},
    React.createElement('div',{className:'row gap-2 hair-b', style:{padding:'6px 10px', background:'var(--paper-1)', fontSize:12}},
      React.createElement('span',{className:'t-mono t-hint'}, 'risk-review/main.ts'),
      React.createElement('div',{className:'grow'}),
      React.createElement('span',{className:'pill warn', style:{padding:'0 6px',fontSize:10}}, '1 error'),
      React.createElement('span',{className:'pill', style:{padding:'0 6px',fontSize:10}}, 'ts 5.4'),
      React.createElement('button',{className:'btn small'}, 'Format'),
      React.createElement('button',{className:'btn small primary'}, React.createElement(Icon.Play), 'Dry-run'),
    ),
    React.createElement('div',{style:{display:'grid', gridTemplateColumns:'44px 1fr', fontFamily:'var(--font-mono)', fontSize:12.5, lineHeight:'20px'}},
      React.createElement('div',{style:{background:'var(--paper-1)', color:'var(--ink-3)', textAlign:'right', padding:'10px 8px 10px 0', borderRight:'1px solid var(--hairline)'}},
        lines.map(l => React.createElement('div',{key:l.n}, l.n))),
      React.createElement('div',{style:{padding:'10px 14px', whiteSpace:'pre', overflowX:'auto'}},
        lines.map(l => React.createElement('div',{key:l.n, style:{color: l.c==='c'?'var(--ink-3)':'var(--ink-1)', background: l.bug?'var(--err-wash)':'transparent', marginLeft:-14, paddingLeft:14}},
          l.t || '\u00A0')))
    ),
    React.createElement('div',{className:'hair-t', style:{padding:'10px 12px', display:'flex', gap:14, fontSize:11.5, color:'var(--ink-2)'}},
      React.createElement('span',null, React.createElement('span',{className:'pill err', style:{padding:'0 6px',fontSize:10}}, 'TS2531'), ' policies.js:47 · Object is possibly null'),
      React.createElement('div',{className:'grow'}),
      React.createElement('span',{className:'t-mono t-hint'}, 'last saved · 12s ago')
    )
  );
}

// --- GAgent form ---
function GAgentForm(){
  const fields = [
    ['Type URL', React.createElement('input',{className:'input t-mono', defaultValue:'type://Aevatar.GAgents.Intake/IntakeGAgent@2', style:{width:'100%'}})],
    ['Display name', React.createElement('input',{className:'input', defaultValue:'Intake', style:{width:'100%'}})],
    ['Role', React.createElement('input',{className:'input', defaultValue:'intake-classifier', style:{width:'100%'}})],
    ['Initial prompt', React.createElement('textarea',{className:'input', rows:3, style:{width:'100%'}, defaultValue:'You are the intake classifier. Identify intent, urgency, language. Route to knowledge or escalation.'})],
    ['Tools',  React.createElement('div',{className:'row gap-2', style:{flexWrap:'wrap'}},
      React.createElement('span',{className:'pill accent'}, 'classify_intent'),
      React.createElement('span',{className:'pill accent'}, 'detect_language'),
      React.createElement('span',{className:'pill'}, '+ add tool')
    )],
    ['State persistence', React.createElement('div',{className:'row gap-3'},
      React.createElement('label',{className:'row gap-2'}, React.createElement('input',{type:'radio',name:'st',defaultChecked:true}), 'Orleans grain'),
      React.createElement('label',{className:'row gap-2'}, React.createElement('input',{type:'radio',name:'st'}), 'Ephemeral'),
    )],
  ];
  return React.createElement('div',{className:'card'},
    React.createElement('div',{className:'card-b', style:{display:'grid', gridTemplateColumns:'140px 1fr', gap:'10px 16px', alignItems:'start'}},
      ...fields.flatMap((row,i) => [
        React.createElement('div',{key:'l'+i, className:'t-label', style:{paddingTop:6}}, row[0]),
        React.createElement('div',{key:'v'+i}, row[1])
      ])
    )
  );
}

function DryRunPane({type}){
  return React.createElement('div',{className:'card', style:{position:'sticky', top:14}},
    React.createElement('div',{className:'card-h'},
      React.createElement('span',{className:'t-title', style:{fontSize:13}}, 'Dry-run'),
      React.createElement('div',{className:'grow'}),
      React.createElement('span',{className:'prov seeded'}, 'seeded fixture')
    ),
    React.createElement('div',{className:'card-b'},
      React.createElement('div',{className:'t-label'}, 'Sample input'),
      React.createElement('textarea',{className:'input', rows:5, style:{width:'100%', marginTop:6},
        defaultValue: JSON.stringify({channel:'telegram',text:'refund for order #92817 — 3rd time asking',user:'alex'}, null, 2)}),
      React.createElement('div',{className:'row gap-2', style:{marginTop:10}},
        React.createElement('button',{className:'btn primary'}, React.createElement(Icon.Play), 'Run'),
        React.createElement('button',{className:'btn'}, 'Load fixture'),
      ),
      React.createElement('div',{className:'t-label', style:{marginTop:14}}, 'Output'),
      React.createElement('pre',{className:'t-mono', style:{margin:'6px 0 0', padding:10, background:'var(--paper-1)', border:'1px solid var(--hairline)', borderRadius:6, fontSize:11.5, maxHeight:180, overflow:'auto'}},
        type==='workflow' ? '{\n  "intent": "refund",\n  "priority": "high",\n  "routes": ["knowledge","risk"],\n  "elapsed_ms": 1260\n}'
        : type==='script' ? 'TypeError: Cannot read property \'total\' of null\n    at checkRefundLimit (policies.js:47)\n    at pipeline (policies.js:12)'
        : '{\n  "classify_intent": { "intent":"refund", "confidence":0.94 },\n  "detect_language": "en"\n}'
      ),
      type==='script' && React.createElement('div',{className:'row gap-2', style:{marginTop:8}},
        React.createElement('span',{className:'pill err', style:{padding:'0 6px',fontSize:10}}, 'error'),
        React.createElement('span',{className:'t-hint', style:{fontSize:11}}, 'fix null-check on order before cap calc')
      )
    )
  );
}

function BuildStep({type, onType, onContinue, dag}){
  const [selectedNode, setSelectedNode] = useState('intake');
  return React.createElement('div',{style:{padding:16, display:'grid', gridTemplateColumns:'1fr 320px', gap:16}},
    React.createElement('div',{className:'col gap-3'},
      React.createElement(BuildTypeSelector,{type, onChange:onType}),
      React.createElement('div',{className:'col gap-2'},
        React.createElement('div',{className:'row gap-2'},
          React.createElement('span',{className:'t-label'}, type==='workflow' ? 'DAG canvas' : type==='script' ? 'Script source' : 'GAgent definition'),
          React.createElement('div',{className:'grow'}),
          type==='workflow' && React.createElement('span',{className:'prov live'}, 'canvas · live'),
          type==='script'   && React.createElement('span',{className:'prov partial'}, 'lints · partial'),
          type==='gagent'   && React.createElement('span',{className:'prov seeded'}, 'template · seeded'),
        ),
        type==='workflow' && React.createElement(WorkflowCanvas,{dag, selected:selectedNode, onSelectNode:setSelectedNode}),
        type==='script'   && React.createElement(ScriptEditor),
        type==='gagent'   && React.createElement(GAgentForm)
      ),
      type==='workflow' && React.createElement('div',{className:'card'},
        React.createElement('div',{className:'card-h'},
          React.createElement('span',{className:'t-title', style:{fontSize:13}}, 'Step · ',
            dag.nodes.find(n=>n.id===selectedNode)?.label),
          React.createElement('div',{className:'grow'}),
          React.createElement('span',{className:'pill'}, dag.nodes.find(n=>n.id===selectedNode)?.kind),
        ),
        React.createElement('div',{className:'card-b', style:{display:'grid', gridTemplateColumns:'1fr 1fr', gap:16}},
          React.createElement('div',null,
            React.createElement('div',{className:'t-label'}, 'Role'),
            React.createElement('div',{style:{marginTop:4}}, dag.nodes.find(n=>n.id===selectedNode)?.role),
            React.createElement('div',{className:'t-label', style:{marginTop:12}}, 'Implementation'),
            React.createElement('div',{className:'t-mono', style:{marginTop:4, fontSize:12}},
              `type://Aevatar.GAgents.${(dag.nodes.find(n=>n.id===selectedNode)?.label||'').replace(/\s/g,'')}GAgent@3`)
          ),
          React.createElement('div',null,
            React.createElement('div',{className:'t-label'}, 'Inputs'),
            React.createElement('pre',{className:'t-mono', style:{margin:'4px 0 0',padding:8,background:'var(--paper-1)',border:'1px solid var(--hairline)',borderRadius:6,fontSize:11.5}},
              '{ message:string, locale:"en"|"zh", user:UserRef }'),
            React.createElement('div',{className:'t-label', style:{marginTop:10}}, 'Outputs'),
            React.createElement('pre',{className:'t-mono', style:{margin:'4px 0 0',padding:8,background:'var(--paper-1)',border:'1px solid var(--hairline)',borderRadius:6,fontSize:11.5}},
              '{ intent:string, priority:"low"|"med"|"high" }')
          )
        )
      ),
      React.createElement('div',{className:'row gap-2'},
        React.createElement('button',{className:'btn'}, 'Save draft'),
        React.createElement('div',{className:'grow'}),
        React.createElement('span',{className:'t-hint'}, 'Ready to bind — generates an invoke URL on next step'),
        React.createElement('button',{className:'btn primary', onClick:onContinue},
          'Continue to Bind', React.createElement(Icon.Chevron))
      )
    ),
    React.createElement(DryRunPane,{type})
  );
}
window.BuildStep = BuildStep;
