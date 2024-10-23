"use strict";(self.webpackChunkwebsite=self.webpackChunkwebsite||[]).push([[3926],{9145:(e,n,r)=>{r.r(n),r.d(n,{assets:()=>d,contentTitle:()=>i,default:()=>u,frontMatter:()=>o,metadata:()=>c,toc:()=>a});var t=r(4848),s=r(8453);const o={id:"procedure",sidebar_label:"Procedures",title:"Procedures"},i="Server-Side Procedures",c={id:"extensions/procedure",title:"Procedures",description:"Custom procedures allow adding a new non-transactional procedure and registering it with Garnet. This registered procedure can then be invoked from any Garnet client to perform a multi-command non-transactional operation on the Garnet server.",source:"@site/docs/extensions/procedure.md",sourceDirName:"extensions",slug:"/extensions/procedure",permalink:"/garnet/docs/extensions/procedure",draft:!1,unlisted:!1,editUrl:"https://github.com/microsoft/garnet/tree/main/website/docs/extensions/procedure.md",tags:[],version:"current",frontMatter:{id:"procedure",sidebar_label:"Procedures",title:"Procedures"},sidebar:"garnetDocSidebar",previous:{title:"Transactions",permalink:"/garnet/docs/extensions/transactions"},next:{title:"Modules",permalink:"/garnet/docs/extensions/module"}},d={},a=[{value:"Developing custom server side procedures",id:"developing-custom-server-side-procedures",level:3}];function l(e){const n={a:"a",admonition:"admonition",code:"code",h1:"h1",h3:"h3",header:"header",li:"li",mdxAdmonitionTitle:"mdxAdmonitionTitle",p:"p",strong:"strong",ul:"ul",...(0,s.R)(),...e.components};return(0,t.jsxs)(t.Fragment,{children:[(0,t.jsx)(n.header,{children:(0,t.jsx)(n.h1,{id:"server-side-procedures",children:"Server-Side Procedures"})}),"\n",(0,t.jsx)(n.p,{children:"Custom procedures allow adding a new non-transactional procedure and registering it with Garnet. This registered procedure can then be invoked from any Garnet client to perform a multi-command non-transactional operation on the Garnet server."}),"\n",(0,t.jsx)(n.h3,{id:"developing-custom-server-side-procedures",children:"Developing custom server side procedures"}),"\n",(0,t.jsxs)(n.p,{children:[(0,t.jsx)(n.code,{children:"CustomProcedure"})," is the base class for all custom procedures. To develop a new one, this class has to be extended and then include the custom logic. There is one method to be implemented in a new custom procedure:"]}),"\n",(0,t.jsxs)(n.ul,{children:["\n",(0,t.jsx)(n.li,{children:(0,t.jsx)(n.code,{children:"Execute<TGarnetApi>(TGarnetApi garnetApi, ArgSlice input, ref MemoryResult<byte> output)"})}),"\n"]}),"\n",(0,t.jsxs)(n.p,{children:["The ",(0,t.jsx)(n.code,{children:"Execute"})," method has the core logic of the custom procedure. Its implementation could process input passed in through the (",(0,t.jsx)(n.code,{children:"input"}),") parameter and perform operations on Garnet by invoking any of the APIs available on ",(0,t.jsx)(n.code,{children:"IGarnetApi"}),". This method then generates the output of the procedure as well."]}),"\n",(0,t.jsxs)(n.p,{children:["These are the helper methods for developing custom procedures same as that of custom transactions detailed ",(0,t.jsx)(n.a,{href:"/garnet/docs/extensions/transactions#developing-custom-server-side-transactions",children:"here"}),"."]}),"\n",(0,t.jsx)(n.p,{children:"Registering the custom procedure is done on the server-side by calling the"}),"\n",(0,t.jsx)(n.p,{children:(0,t.jsx)(n.code,{children:"NewProcedure(string name, CustomProcedure customProcedure, RespCommandsInfo commandInfo = null)"})}),"\n",(0,t.jsxs)(n.p,{children:["method on the Garnet server object's ",(0,t.jsx)(n.code,{children:"RegisterAPI"})," object with its name, an instance of the custom procedure class and optional commandInfo."]}),"\n",(0,t.jsxs)(n.p,{children:[(0,t.jsx)(n.strong,{children:"NOTE"})," When invoking APIs on ",(0,t.jsx)(n.code,{children:"IGarnetApi"})," multiple times with large outputs, it is possible to exhaust the internal buffer capacity. If such usage scenarios are expected, the buffer could be reset as described below."]}),"\n",(0,t.jsxs)(n.ul,{children:["\n",(0,t.jsxs)(n.li,{children:["Retrieve the initial buffer offset using ",(0,t.jsx)(n.code,{children:"IGarnetApi.GetScratchBufferOffset"})]}),"\n",(0,t.jsxs)(n.li,{children:["Invoke necessary apis on ",(0,t.jsx)(n.code,{children:"IGarnetApi"})]}),"\n",(0,t.jsxs)(n.li,{children:["Reset the buffer back to where it was using ",(0,t.jsx)(n.code,{children:"IGarnetApi.ResetScratchBuffer(offset)"})]}),"\n"]}),"\n",(0,t.jsxs)(n.admonition,{type:"tip",children:[(0,t.jsx)(n.mdxAdmonitionTitle,{}),(0,t.jsx)(n.p,{children:"As a reference of an implementation of a custom procedure, see the example in GarnetServer\\Extensions\\Sum.cs."})]})]})}function u(e={}){const{wrapper:n}={...(0,s.R)(),...e.components};return n?(0,t.jsx)(n,{...e,children:(0,t.jsx)(l,{...e})}):l(e)}},8453:(e,n,r)=>{r.d(n,{R:()=>i,x:()=>c});var t=r(6540);const s={},o=t.createContext(s);function i(e){const n=t.useContext(o);return t.useMemo((function(){return"function"==typeof e?e(n):{...n,...e}}),[n,e])}function c(e){let n;return n=e.disableParentContext?"function"==typeof e.components?e.components(s):e.components||s:i(e.components),t.createElement(o.Provider,{value:n},e.children)}}}]);