// App root — shell + routing between build/bind/invoke/observe

function App(){
  const [services] = useState(window.DEMO.services);
  const [activeId, setActiveId] = useState(services[0].id);
  const svc = services.find(s => s.id === activeId) || services[0];
  const initialStep = svc.binding==='bound' ? 'invoke' : svc.binding==='draft' ? 'bind' : 'build';
  const [step, setStep] = useState(initialStep);
  const [buildType, setBuildType] = useState(svc.type);

  useEffect(()=>{ setBuildType(svc.type); setStep(svc.binding==='bound' ? 'invoke' : svc.binding==='draft' ? 'bind' : 'build'); }, [activeId]);

  // tweaks
  const [tweaks, setTweaks] = useState({accent:'blue', theme:'light', density:'cozy', aguiMode:'timeline', layout:'split'});
  const [tweaksOpen, setTweaksOpen] = useState(false);
  useEffect(()=>{
    const root = document.documentElement;
    root.setAttribute('data-accent', tweaks.accent);
    root.setAttribute('data-theme',  tweaks.theme);
    root.setAttribute('data-density', tweaks.density);
    window.TWEAKS = tweaks;
  }, [tweaks]);

  const steps = [
    {key:'build',   label:'Build',   done: svc.binding!=='unbound'},
    {key:'bind',    label:'Bind',    done: svc.binding==='bound'},
    {key:'invoke',  label:'Invoke',  done: svc.binding==='bound' && svc.lastRun !== '—'},
    {key:'observe', label:'Observe', done: svc.binding==='bound' && svc.lastRun !== '—'},
  ];

  const content = step==='build'
      ? React.createElement(BuildStep,{type:buildType, onType:setBuildType, dag:window.DEMO.dag, onContinue:()=>setStep('bind')})
    : step==='bind'
      ? React.createElement(BindStep,{service:svc, onContinue:()=>setStep('invoke')})
    : step==='invoke'
      ? React.createElement(InvokeStep,{service:svc})
    : React.createElement(ObserveStep,{service:svc});

  return React.createElement('div',{style:{display:'flex', flexDirection:'column', height:'100vh', minHeight:0}},
    React.createElement('div',{className:'hair-b', style:{background:'var(--paper-0)'}},
      React.createElement(TopBar,{onToggleTweaks:()=>setTweaksOpen(o=>!o), tweaksOpen})),
    React.createElement('div',{style:{display:'flex', flex:1, minHeight:0, background:'var(--paper-1)'}},
      React.createElement('div',{className:'hair-r', style:{width:280, flexShrink:0, background:'var(--paper-0)', display:'flex', flexDirection:'column', minHeight:0}},
        React.createElement(ServiceList,{services, activeId, onSelect:setActiveId})),
      React.createElement('div',{style:{flex:1, display:'flex', flexDirection:'column', minWidth:0, minHeight:0, overflow:'auto'}},
        React.createElement(ServiceHeader,{service:svc, step, onStep:setStep, steps}),
        React.createElement('div',{style:{flex:1, minHeight:0, display:'flex', flexDirection:'column', background:'var(--paper-1)'}},
          content)
      )
    ),
    tweaksOpen && React.createElement(Tweaks,{tweaks, setTweaks, onClose:()=>setTweaksOpen(false)})
  );
}

const root = ReactDOM.createRoot(document.getElementById('root'));
root.render(React.createElement(App));
