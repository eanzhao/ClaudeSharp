// Support Escalation Triage — demo content & fixtures
window.DEMO = (function(){
  const now = Date.now();
  const t = (sec) => new Date(now - sec*1000).toISOString();

  const services = [
    {
      id: "svc-triage-v3",
      name: "support-triage",
      display: "Support Triage Router",
      type: "workflow",
      revision: "r-0f2a · v3",
      binding: "bound",
      health: "degraded",
      lastRun: "2m ago",
      owner: "runtime-ops",
      url: "https://api.aevatar.io/svc/sc-38f2/support-triage",
      description: "Routes inbound tickets through intake → knowledge lookup → risk review → escalation decision."
    },
    {
      id: "svc-knowledge",
      name: "knowledge-answer",
      display: "Knowledge Drafter",
      type: "gagent",
      revision: "r-2817 · v7",
      binding: "bound",
      health: "healthy",
      lastRun: "14s ago",
      owner: "kb-team",
      url: "https://api.aevatar.io/svc/sc-38f2/knowledge-answer"
    },
    {
      id: "svc-risk",
      name: "risk-review",
      display: "Risk & Policy Review",
      type: "script",
      revision: "r-1c03 · v2",
      binding: "draft",
      health: "attention",
      lastRun: "—",
      owner: "compliance"
    },
    {
      id: "svc-escalate",
      name: "escalation-decider",
      display: "Escalation Decider",
      type: "gagent",
      revision: "r-5501 · v1",
      binding: "bound",
      health: "healthy",
      lastRun: "3m ago",
      owner: "ops"
    },
    {
      id: "svc-digest",
      name: "shift-digest",
      display: "Shift Digest",
      type: "workflow",
      revision: "draft",
      binding: "unbound",
      health: "unknown",
      lastRun: "—",
      owner: "runtime-ops"
    },
  ];

  const scope = {
    id: "sc-38f2",
    name: "support-ops",
    displayName: "Support Ops",
    environment: "staging"
  };

  // Workflow DAG for support-triage
  const dag = {
    nodes: [
      { id: "intake",     label: "Intake",            kind: "gagent",   x: 80,  y: 60, role: "Classify intent, priority" },
      { id: "knowledge",  label: "Knowledge Drafter", kind: "gagent",   x: 300, y: 60, role: "Draft KB-grounded reply" },
      { id: "risk",       label: "Risk & Policy",     kind: "script",   x: 300, y: 200, role: "Refund / SLA / policy checks" },
      { id: "escalate",   label: "Escalation Decide", kind: "gagent",   x: 540, y: 130, role: "Auto-reply or human hand-off" },
      { id: "telegram",   label: "Telegram",          kind: "external", x: 760, y: 60,  role: "Channel out" },
      { id: "human",      label: "Human Agent",       kind: "external", x: 760, y: 200, role: "On-call shift" },
    ],
    edges: [
      { from: "intake", to: "knowledge" },
      { from: "intake", to: "risk" },
      { from: "knowledge", to: "escalate" },
      { from: "risk", to: "escalate" },
      { from: "escalate", to: "telegram", label:"auto" },
      { from: "escalate", to: "human", label:"hand-off" },
    ]
  };

  // AGUI event stream for a run
  const runEvents = [
    { t: 0,     kind:"run.start",    label:"Run started",        step:"intake",    detail:"message_id=tg_9281 · user=alex" },
    { t: 120,   kind:"step.start",   label:"intake · classify",  step:"intake",    detail:"Intake GAgent booting" },
    { t: 380,   kind:"thinking",     label:"model thinking",     step:"intake",    detail:"classifying intent · 'refund'", thinking:"The user mentions a failed payment 3 days ago. Likely intent=refund. Priority=high because of repeat contact. Language=en."},
    { t: 720,   kind:"tool.call",    label:"tool · classify_intent", step:"intake", detail:"args={text:…, locale:'en'}", tool:"classify_intent", args:{text:"refund for order #92817 — 3rd time asking", locale:"en"} },
    { t: 980,   kind:"tool.result",  label:"tool · classify_intent", step:"intake", detail:"{intent:'refund', priority:'high'}", tool:"classify_intent", result:{intent:"refund",priority:"high",confidence:0.94} },
    { t: 1240,  kind:"step.done",    label:"intake · done",      step:"intake",    detail:"→ knowledge, → risk" },
    { t: 1260,  kind:"handoff",      label:"hand-off",           step:"intake→knowledge,risk", detail:"parallel fan-out" },
    { t: 1400,  kind:"step.start",   label:"knowledge · draft",  step:"knowledge", detail:"grounded retrieval" },
    { t: 1720,  kind:"tool.call",    label:"tool · kb_search",   step:"knowledge", detail:"q='refund order 92817'", tool:"kb_search", args:{query:"refund failed payment order 92817"} },
    { t: 2050,  kind:"tool.result",  label:"tool · kb_search",   step:"knowledge", detail:"3 docs returned", tool:"kb_search", result:{docs:[{id:"kb-441",title:"Failed payment refunds"},{id:"kb-207",title:"Order lookup by id"},{id:"kb-88",title:"Tone · empathetic"}]} },
    { t: 2340,  kind:"step.start",   label:"risk · policy check",step:"risk",      detail:"script running" },
    { t: 2610,  kind:"ctx.change",   label:"context updated",    step:"risk",      detail:"risk.sla_breach=true" },
    { t: 2880,  kind:"step.error",   label:"risk · script error",step:"risk",      detail:"Script: Cannot read property 'total' of null  (policies.js:47)", error:"TypeError: Cannot read property 'total' of null\n    at checkRefundLimit (policies.js:47:20)\n    at pipeline (policies.js:12:5)" },
    { t: 2980,  kind:"retry",        label:"retry risk (1/2)",   step:"risk",      detail:"back-off 800ms" },
    { t: 3820,  kind:"step.done",    label:"risk · done",        step:"risk",      detail:"{sla_breach:true, refund_ok:true, cap:$140}" },
    { t: 4120,  kind:"step.done",    label:"knowledge · done",   step:"knowledge", detail:"draft=192 tokens" },
    { t: 4260,  kind:"step.start",   label:"escalate · decide",  step:"escalate",  detail:"weighting signals" },
    { t: 4580,  kind:"human.request",label:"human approval requested", step:"escalate", detail:"Refund $140 to alex · needs L2 sign-off", prompt:"Approve $140 refund for order #92817? Customer has contacted support 3x. SLA breached by 6h. KB-441 applies.", requestId:"apr_0a91" },
    // run pauses here in default view
  ];

  const history = [
    { id:"run_9183", when:"2 min ago", status:"waiting", input:{channel:"telegram", text:"refund for order #92817 — 3rd time asking", user:"alex"}, note:"awaiting L2 approval" },
    { id:"run_9182", when:"14 min ago", status:"ok",       input:{channel:"web", text:"where is my order?", user:"maya"}, note:"auto-reply" },
    { id:"run_9179", when:"31 min ago", status:"ok",       input:{channel:"telegram", text:"can I change shipping address?", user:"tomas"}, note:"auto-reply · KB-502" },
    { id:"run_9177", when:"44 min ago", status:"error",    input:{channel:"web", text:"account locked help", user:"june"}, note:"risk script error" },
    { id:"run_9170", when:"1h ago",     status:"ok",       input:{channel:"telegram", text:"invoice please", user:"dmitri"}, note:"auto-reply" },
    { id:"run_9164", when:"1h 12m ago", status:"ok",       input:{channel:"web", text:"bulk discount?", user:"sales-ops"}, note:"handed off · sales" },
  ];

  const bindings = [
    { id:"bind_01", alias:"prod", scope:"support-ops", revision:"r-0f2a · v3", url:"https://api.aevatar.io/svc/sc-38f2/support-triage", rate:"60 rpm", status:"serving", since:"6d" },
    { id:"bind_02", alias:"staging", scope:"support-ops", revision:"r-0f2a · v3", url:"https://api.aevatar.io/svc/sc-38f2/support-triage?env=staging", rate:"unlimited", status:"serving", since:"2h" },
    { id:"bind_03", alias:"canary-v4", scope:"support-ops", revision:"r-11bb · v4-rc", url:"https://api.aevatar.io/svc/sc-38f2/support-triage?env=canary", rate:"5 rpm", status:"paused", since:"1d" },
  ];

  return {scope, services, dag, runEvents, history, bindings};
})();
