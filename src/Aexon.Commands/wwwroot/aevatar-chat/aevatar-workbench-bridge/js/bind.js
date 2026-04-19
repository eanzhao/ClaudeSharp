// Bind step — invoke URL, params, cURL/Fetch/SDK tabs, bindings list

function BindStep({service, onContinue}){
  const [env, setEnv] = useState('staging');
  const [rev, setRev] = useState(service.revision);
  const [tab, setTab] = useState('curl');
  const url = service.url || `https://api.aevatar.io/svc/sc-38f2/${service.name}`;
  const token = 'nyxid_7c2f9…a01b';

  const curl = `curl -X POST "${url}" \\
  -H "Authorization: Bearer ${token}" \\
  -H "Content-Type: application/json" \\
  -H "X-Aevatar-Scope: sc-38f2" \\
  -d '{"channel":"telegram","text":"refund for order #92817","user":"alex"}'`;

  const fetchJs = `const res = await fetch("${url}", {
  method: "POST",
  headers: {
    "Authorization": \`Bearer \${nyxidToken}\`,
    "Content-Type": "application/json",
    "X-Aevatar-Scope": "sc-38f2",
    "Accept": "text/event-stream",
  },
  body: JSON.stringify({
    channel: "telegram",
    text: "refund for order #92817",
    user: "alex",
  }),
});

// AGUI events stream as SSE
for await (const event of parseAguiStream(res.body)) {
  console.log(event.kind, event);
}`;

  const sdk = `import { AevatarClient } from "@aevatar/sdk";

const client = new AevatarClient({
  token: nyxidToken,          // NyxID bearer
  scope: "sc-38f2",
});

const run = client.service("${service.name}")
  .invoke({ channel: "telegram", text: "…", user: "alex" });

run.on("step", (e) => …);
run.on("tool.call", (e) => …);
run.on("human.request", async (e) => {
  await run.respond(e.requestId, { approved: true });
});

const result = await run.done();`;

  const snippet = tab==='curl' ? curl : tab==='fetch' ? fetchJs : sdk;

  return React.createElement('div',{style:{padding:16, display:'grid', gridTemplateColumns:'1fr 380px', gap:16}},
    React.createElement('div',{className:'col gap-3'},
      // URL card
      React.createElement('div',{className:'card'},
        React.createElement('div',{className:'card-h'},
          React.createElement('span',{className:'t-title', style:{fontSize:13}}, 'Invoke URL'),
          React.createElement('span',{className:'pill ok'}, 'bound · live'),
          React.createElement('div',{className:'grow'}),
          React.createElement('span',{className:'prov live'}, 'gateway · live'),
        ),
        React.createElement('div',{className:'card-b'},
          React.createElement('div',{style:{display:'flex', alignItems:'stretch', border:'1px solid var(--hairline)', borderRadius:8, overflow:'hidden'}},
            React.createElement('span',{style:{padding:'0 10px', background:'var(--paper-1)', fontFamily:'var(--font-mono)', fontSize:12, color:'var(--ink-3)', display:'flex', alignItems:'center', borderRight:'1px solid var(--hairline)'}}, 'POST'),
            React.createElement('div',{className:'t-mono', style:{flex:1, padding:'10px 12px', fontSize:12.5, color:'var(--ink-0)', overflowX:'auto', whiteSpace:'nowrap'}}, url),
            React.createElement(CopyBtn,{text:url})
          ),
          React.createElement('div',{className:'row gap-2', style:{marginTop:10, flexWrap:'wrap'}},
            React.createElement('span',{className:'pill'}, React.createElement(Icon.User), 'auth · Bearer ', React.createElement('span',{className:'t-mono',style:{marginLeft:4,color:'var(--ink-0)'}}, token)),
            React.createElement('span',{className:'pill'}, 'scope · sc-38f2'),
            React.createElement('span',{className:'pill'}, 'revision · ', rev),
            React.createElement('span',{className:'pill copper'}, 'stream · text/event-stream'),
          ),
          React.createElement('div',{style:{marginTop:14, background:'var(--copper-wash)', border:'1px solid var(--copper-wash)', borderRadius:6, padding:'8px 10px', display:'flex', gap:8, alignItems:'flex-start', fontSize:12}},
            React.createElement('span',{style:{marginTop:2,color:'var(--copper-ink)'}}, '⌇'),
            React.createElement('div',{className:'col gap-1'},
              React.createElement('span',{style:{color:'var(--copper-ink)', fontWeight:600}}, 'Need a NyxID token?'),
              React.createElement('span',{style:{color:'var(--ink-1)'}}, 'Tokens live in your NyxID wallet. Visit ', React.createElement('a',{href:'#',style:{color:'var(--accent)'}},'nyxid.io/tokens'), ' → create scoped token → paste as the ',React.createElement('code',{className:'t-mono'},'Authorization'),' bearer. Tokens rotate every 24h.')
            )
          )
        )
      ),

      // Binding params form
      React.createElement('div',{className:'card'},
        React.createElement('div',{className:'card-h'},
          React.createElement('span',{className:'t-title', style:{fontSize:13}}, 'Binding parameters')),
        React.createElement('div',{className:'card-b', style:{display:'grid', gridTemplateColumns:'140px 1fr 140px 1fr', gap:'10px 14px', alignItems:'center'}},
          React.createElement('div',{className:'t-label'}, 'Scope'),
          React.createElement('select',{className:'input'}, React.createElement('option',null,'support-ops · sc-38f2')),
          React.createElement('div',{className:'t-label'}, 'Environment'),
          React.createElement('div',{className:'row gap-1'},
            ['dev','staging','prod'].map(e =>
              React.createElement('button',{key:e, onClick:()=>setEnv(e), className:'btn small', style:{background: env===e?'var(--ink-0)':'transparent', color: env===e?'var(--paper-0)':'var(--ink-1)', borderColor: env===e?'var(--ink-0)':'var(--hairline)'}}, e))
          ),
          React.createElement('div',{className:'t-label'}, 'Revision'),
          React.createElement('select',{className:'input', value:rev, onChange:(e)=>setRev(e.target.value)},
            [service.revision, 'r-11bb · v4-rc', 'r-0d11 · v2'].map(r=>React.createElement('option',{key:r}, r))),
          React.createElement('div',{className:'t-label'}, 'Rate limit'),
          React.createElement('input',{className:'input', defaultValue:'60 rpm'}),
          React.createElement('div',{className:'t-label'}, 'Allowed origins'),
          React.createElement('input',{className:'input', defaultValue:'app.aevatar.io, *.support.acme.io'}),
          React.createElement('div',{className:'t-label'}, 'Streaming'),
          React.createElement('div',{className:'row gap-3', style:{fontSize:12}},
            React.createElement('label',{className:'row gap-2'}, React.createElement('input',{type:'checkbox',defaultChecked:true}), 'SSE'),
            React.createElement('label',{className:'row gap-2'}, React.createElement('input',{type:'checkbox'}), 'WebSocket'),
            React.createElement('label',{className:'row gap-2'}, React.createElement('input',{type:'checkbox',defaultChecked:true}), 'AGUI frames'),
          ),
        )
      ),

      // Snippet tabs
      React.createElement('div',{className:'card'},
        React.createElement('div',{className:'card-h', style:{paddingBottom:0}},
          React.createElement('div',{className:'tabs', style:{border:'none'}},
            ['curl','fetch','sdk'].map(k =>
              React.createElement('button',{key:k, onClick:()=>setTab(k), className:'tab ' + (tab===k?'active':'')}, k.toUpperCase()))
          ),
          React.createElement('div',{className:'grow'}),
          React.createElement(CopyBtn,{text:snippet})
        ),
        React.createElement('pre',{className:'t-mono', style:{margin:0, padding:'12px 14px', background:'var(--paper-1)', borderTop:'1px solid var(--hairline)', fontSize:12, overflowX:'auto', lineHeight:1.55, color:'var(--ink-0)'}}, snippet)
      ),

      // Existing bindings list
      React.createElement('div',{className:'card'},
        React.createElement('div',{className:'card-h'},
          React.createElement('span',{className:'t-title', style:{fontSize:13}}, 'Existing bindings'),
          React.createElement('span',{className:'pill'}, window.DEMO.bindings.length),
          React.createElement('div',{className:'grow'}),
          React.createElement('span',{className:'prov live'}, 'governance · live'),
        ),
        React.createElement('table',{style:{width:'100%', borderCollapse:'collapse', fontSize:12}},
          React.createElement('thead',null, React.createElement('tr',{style:{background:'var(--paper-1)'}},
            ['Alias','Scope · Env','Revision','URL','Rate','Status','Actions'].map(h =>
              React.createElement('th',{key:h, style:{textAlign:'left', padding:'8px 12px', fontWeight:500, color:'var(--ink-2)', fontSize:11, textTransform:'uppercase', letterSpacing:'.06em', borderBottom:'1px solid var(--hairline)'}}, h))
          )),
          React.createElement('tbody',null,
            window.DEMO.bindings.map(b =>
              React.createElement('tr',{key:b.id, style:{borderBottom:'1px solid var(--hairline-soft)'}},
                React.createElement('td',{style:{padding:'10px 12px', fontWeight:500, color:'var(--ink-0)'}}, b.alias),
                React.createElement('td',{style:{padding:'10px 12px'}}, b.scope),
                React.createElement('td',{style:{padding:'10px 12px'}}, React.createElement('span',{className:'t-mono', style:{fontSize:11.5}}, b.revision)),
                React.createElement('td',{style:{padding:'10px 12px', maxWidth:280, overflow:'hidden', textOverflow:'ellipsis', whiteSpace:'nowrap'}}, React.createElement('span',{className:'t-mono t-hint', style:{fontSize:11.5}}, b.url)),
                React.createElement('td',{style:{padding:'10px 12px'}}, b.rate),
                React.createElement('td',{style:{padding:'10px 12px'}},
                  b.status==='serving' ? React.createElement('span',{className:'pill ok'}, 'serving')
                                        : React.createElement('span',{className:'pill warn'}, b.status)),
                React.createElement('td',{style:{padding:'10px 12px'}},
                  React.createElement('div',{className:'row gap-1'},
                    React.createElement('button',{className:'btn small ghost'}, 'Rotate'),
                    React.createElement('button',{className:'btn small ghost'}, 'Revoke'),
                  ))
              ))
          )
        )
      ),

      React.createElement('div',{className:'row gap-2'},
        React.createElement('button',{className:'btn'}, '← Back to Build'),
        React.createElement('div',{className:'grow'}),
        React.createElement('span',{className:'t-hint'}, 'Try it now in the Playground'),
        React.createElement('button',{className:'btn primary', onClick:onContinue},
          'Continue to Invoke', React.createElement(Icon.Chevron))
      )
    ),

    // Right rail — test in place
    React.createElement('div',{className:'col gap-3'},
      React.createElement('div',{className:'card', style:{position:'sticky', top:14}},
        React.createElement('div',{className:'card-h'},
          React.createElement('span',{className:'t-title', style:{fontSize:13}}, 'Smoke-test'),
          React.createElement('div',{className:'grow'}),
          React.createElement('span',{className:'prov seeded'}, 'token · seeded')
        ),
        React.createElement('div',{className:'card-b'},
          React.createElement('div',{className:'t-label'}, 'Bearer token'),
          React.createElement('div',{className:'row gap-2', style:{marginTop:4}},
            React.createElement('input',{className:'input t-mono', defaultValue:token, style:{flex:1, fontSize:11.5}}),
            React.createElement('button',{className:'btn small'}, 'Paste')),
          React.createElement('div',{className:'t-label', style:{marginTop:12}}, 'Body'),
          React.createElement('textarea',{className:'input', rows:5, style:{width:'100%', marginTop:4, fontSize:11.5},
            defaultValue:'{\n  "channel": "telegram",\n  "text": "refund for #92817",\n  "user": "alex"\n}'}),
          React.createElement('button',{className:'btn primary', style:{width:'100%', marginTop:10, justifyContent:'center'}},
            React.createElement(Icon.Play), 'Send test request'),
          React.createElement('div',{style:{marginTop:10, padding:'8px 10px', background:'var(--ok-wash)', borderRadius:6, fontSize:12, color:'var(--ok)', display:'flex', gap:6}},
            React.createElement(Icon.Check), '200 OK · 184ms · 6 AGUI frames received'),
          React.createElement('div',{className:'t-hint', style:{marginTop:8, fontSize:11}}, 'Full event stream visible on the next step.')
        )
      )
    )
  );
}
window.BindStep = BindStep;
