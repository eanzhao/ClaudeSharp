import React, { useState, useEffect } from 'react';
import ReactDOM from 'react-dom/client';

// Bridge UMD-style globals expected by the design's JSX files
// (which were originally written for React UMD + Babel-standalone).
window.React = React;
window.ReactDOM = ReactDOM;
window.useState = useState;
window.useEffect = useEffect;

// Demo fixtures attach to window.DEMO via IIFE.
import './js/demo-data.js';
import './styles/tokens.css';

// Component definitions (each file exports nothing; they declare
// components on window or via top-level function decls).
import './js/atoms.jsx';
import './js/chrome.jsx';
import './js/build.jsx';
import './js/bind.jsx';
import './js/invoke.jsx';
import './js/observe.jsx';
import './js/tweaks.jsx';
// app.jsx contains ReactDOM.createRoot(...).render(...) — must be last.
import './js/app.jsx';
