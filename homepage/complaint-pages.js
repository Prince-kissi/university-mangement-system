(function () {
  "use strict";
  const esc=value=>String(value??"").replace(/[&<>'"]/g,char=>({"&":"&amp;","<":"&lt;",">":"&gt;","'":"&#39;",'"':"&quot;"}[char]));
  async function request(path, options={}) {
    const response=await fetch(path,{credentials:"include",cache:"no-store",...options});
    if(!response.ok){let message=`Request failed (${response.status}).`;try{const b=await response.json();message=b.detail||b.message||b.title||message;}catch{}const error=new Error(message);error.status=response.status;throw error;}
    return response.status===204?null:response.json();
  }
  async function write(path,body){const csrf=await request("/api/auth/csrf");return request(path,{method:"POST",headers:{"Content-Type":"application/json","X-CSRF-TOKEN":csrf.token},body:JSON.stringify(body)});}
  const form=document.querySelector("#submitComplaintForm");
  if(form){
    let authenticatedStudent=false;
    let administrativeUnits=[];
    request("/api/students/me").then(()=>{authenticatedStudent=true;}).catch(()=>{authenticatedStudent=false;});
    Promise.all([request("/api/departments"),request("/api/categories")]).then(([departments,categories])=>{
      const ds=form.elements.department,cs=form.elements.category;
      administrativeUnits=departments.filter(x=>x.isActive);
      const renderUnits=items=>{const selected=ds.value;ds.innerHTML='<option value="">Select administrative unit</option>'+items.map(x=>`<option value="${x.id}">${esc(x.name)}</option>`).join("");if(items.some(x=>x.id===selected))ds.value=selected;};
      renderUnits(administrativeUnits);
      form.elements.departmentSearch?.addEventListener("input",event=>{const q=event.target.value.trim().toLowerCase();renderUnits(administrativeUnits.filter(x=>x.name.toLowerCase().includes(q)||String(x.code||"").toLowerCase().includes(q)));});
      cs.innerHTML='<option value="">Select category</option>'+categories.filter(x=>x.isActive).map(x=>`<option value="${x.id}">${x.name}</option>`).join("");
    }).catch(error=>{document.querySelector("#submitComplaintMessage").textContent=`Unable to load database options: ${error.message}`;});
    form.addEventListener("submit",async event=>{
      event.preventDefault(); const data=new FormData(form); const anonymous=document.querySelector("#anonToggle")?.checked;
      const priority={low:"Low",medium:"Normal",high:"High",critical:"Critical"}[data.get("severity")]||"Normal";
      const body={title:data.get("title"),description:data.get("description"),categoryId:data.get("category"),departmentId:data.get("department"),priority,isAnonymous:Boolean(anonymous)};
      const message=document.querySelector("#submitComplaintMessage");
      if(!authenticatedStudent){
        try{await request("/api/students/me");authenticatedStudent=true;}
        catch{window.location.href="login.html?returnUrl=submit-complaint.html&reason=student-login-required";return;}
      }
      try{const result=await write("/api/complaints",body);document.querySelector("#scRefCode").textContent=result.complaintId;const date=result.submittedAt?new Intl.DateTimeFormat("en-GH",{dateStyle:"medium",timeStyle:"short",timeZone:"Africa/Accra"}).format(new Date(result.submittedAt)):"Just now";document.querySelector("#scRoutingResult").innerHTML=`<div><dt>Administrative unit</dt><dd>${esc(result.administrativeUnit||administrativeUnits.find(x=>x.id===body.departmentId)?.name||"")}</dd></div><div><dt>Submitted</dt><dd>${esc(date)}</dd></div><div><dt>Status</dt><dd>${esc(result.status||"Submitted")}</dd></div>`;document.querySelector("#scAnonymousConfirmation").hidden=!anonymous;message.classList.add("show");form.reset();}
      catch(error){message.classList.add("show");message.querySelector("strong").textContent="Complaint was not submitted";message.querySelector("p").textContent=error.message;document.querySelector("#scRefCode").textContent="";}
    });
    document.querySelector("#scFileInput")?.addEventListener("change",event=>{const p=event.target.closest(".sc-upload").querySelector("p");p.textContent=event.target.files.length?`${event.target.files.length} file(s) selected`:"Choose evidence files";});
  }
  const trackingForm=document.querySelector("#trackComplaintForm");
  if(trackingForm){
    const trackingStudentProfile=request("/api/students/me").catch(()=>null);
    const showTrackedComplaint=async data=>{
      const student=await trackingStudentProfile;
      const studentSurname=String(student?.lastName||"Student").trim();
      const honorific=student?.gender==="Female"?"Miss":"Master";
      const salutation=`${honorific} ${studentSurname}`;
      const result=document.querySelector("#trackResult"),none=document.querySelector("#tcNoResult");
      const dateTime=value=>new Intl.DateTimeFormat("en-GH",{dateStyle:"medium",timeStyle:"short",timeZone:"Africa/Accra"}).format(new Date(value));
      none.classList.remove("show");document.querySelector("#resRef").textContent=`Reference: ${data.complaintId}`;document.querySelector("#resTitle").textContent=data.title;document.querySelector("#resDept").textContent=`Administrative Unit: ${data.department}`;document.querySelector("#resStatus").textContent=data.status;document.querySelector("#resDate").textContent=new Intl.DateTimeFormat("en-GH",{dateStyle:"medium"}).format(new Date(data.createdAt));document.querySelector("#resSeverity").textContent=data.priority;
      const responses=data.responses||[],staffResponses=responses.filter(r=>r.isStaffResponse);
      document.querySelector("#trackResponses").innerHTML=`<div class="tc-response-head"><div><p class="tc-response-label">OFFICIAL UMAT RESPONSE</p><h3>${staffResponses.length?"Response from "+esc(data.department):"Awaiting department response"}</h3></div><span class="tc-response-count">${staffResponses.length}</span></div>`+(responses.length?responses.map(r=>r.isStaffResponse?`<article class="umat-response-letter"><header class="umat-letterhead"><img src="LOGO2.png" alt="University of Mines and Technology logo"><div><strong>University of Mines and Technology</strong><span>Knowledge, Truth and Excellence</span><small>${esc(r.administrativeUnit||data.department)}</small></div></header><div class="umat-letter-meta"><span><b>Our Ref:</b> ${esc(data.complaintId)}</span><time>${dateTime(r.signedAt||r.createdAt)}</time></div><div class="umat-letter-body"><p>${esc(salutation)},</p><p>${esc(r.body)}</p><p>Yours faithfully,</p><div class="umat-letter-signoff">${r.signatureIncluded?`<img class="tc-response-signature" src="/api/complaints/${encodeURIComponent(data.id)}/responses/${encodeURIComponent(r.id)}/signature" alt="Officer signature used for this response">`:""}${r.initials?`<strong>${esc(r.initials)}</strong>`:""}<b>${esc(r.signedByName||"Department Officer")}</b><span>${esc(r.administrativeUnit||data.department)}</span></div></div><footer>Generated from the UMaT Complaint Management System</footer></article>`:`<article class="tc-owner-response student"><div class="tc-owner-response-meta"><strong>Your follow-up message</strong><time>${dateTime(r.createdAt)}</time></div><p>${esc(r.body)}</p></article>`).join(""):"<p>The responsible administrative unit has not issued a response yet. You will see it here as soon as it is recorded.</p>");
      const pdf=document.querySelector("#downloadResponsePdf");pdf.href=`/api/complaints/${encodeURIComponent(data.id)}/response.pdf`;pdf.hidden=staffResponses.length===0;
      result.classList.add("show");result.scrollIntoView({behavior:"smooth",block:"start"});
      if(staffResponses.length){const responseLink=`/track-complaint.html?id=${data.id}`.toLowerCase();request("/api/notifications").then(notifications=>Promise.all(notifications.filter(n=>!n.isRead&&String(n.link||"").toLowerCase()===responseLink).map(n=>write(`/api/notifications/${encodeURIComponent(n.id)}/read`,{})))).catch(()=>{});}
    };
    trackingForm.addEventListener("submit",async event=>{
      event.preventDefault();const complaintReference=document.querySelector("#trackReference").value.trim();
      const result=document.querySelector("#trackResult"),none=document.querySelector("#tcNoResult");
      try{const matches=await request(`/api/complaints?search=${encodeURIComponent(complaintReference)}&page=1&pageSize=50`),item=(matches.items||[]).find(x=>String(x.complaintId||"").toLowerCase()===complaintReference.toLowerCase());if(!item)throw new Error("No complaint with that reference number was found in your account.");showTrackedComplaint(await request(`/api/complaints/${encodeURIComponent(item.id)}`));}
      catch(error){result.classList.remove("show");none.classList.add("show");const p=none.querySelector("p");if(p)p.textContent=error.status===401||error.status===403?"Sign in to your student account before tracking an identified complaint.":error.message;}
    });
    const directComplaintId=new URLSearchParams(location.search).get("id");
    if(directComplaintId)request(`/api/complaints/${encodeURIComponent(directComplaintId)}`).then(showTrackedComplaint).catch(error=>{const none=document.querySelector("#tcNoResult");none.classList.add("show");const p=none.querySelector("p");if(p)p.textContent=error.message;});
  }
  const anonymousForm=document.querySelector("#anonymousComplaintForm");
  if(anonymousForm){
    let categories=[],administrativeUnits=[];
    Promise.all([request("/api/departments"),request("/api/categories")]).then(([units,data])=>{administrativeUnits=units.filter(x=>x.isActive);categories=data.filter(x=>x.isActive&&x.allowsAnonymous);const unitSelect=anonymousForm.elements.department,categorySelect=anonymousForm.elements.category;const renderUnits=items=>{unitSelect.innerHTML='<option value="">Select administrative unit</option>'+items.map(x=>`<option value="${x.id}">${esc(x.name)}</option>`).join("");};renderUnits(administrativeUnits);const renderCategories=()=>{categorySelect.innerHTML='<option value="">Select a category</option>'+categories.filter(x=>!x.departmentId||x.departmentId===unitSelect.value).map(x=>`<option value="${x.id}">${esc(x.name)}</option>`).join("");};unitSelect.addEventListener("change",renderCategories);anonymousForm.elements.departmentSearch?.addEventListener("input",event=>{const q=event.target.value.trim().toLowerCase();renderUnits(administrativeUnits.filter(x=>x.name.toLowerCase().includes(q)||String(x.code||"").toLowerCase().includes(q)));renderCategories();});renderCategories();}).catch(error=>{document.querySelector("#anonymousComplaintMessage").textContent=`Unable to load database options: ${error.message}`;});
    anonymousForm.addEventListener("submit",async event=>{event.preventDefault();const f=new FormData(anonymousForm),category=categories.find(x=>x.id===f.get("category")),message=document.querySelector("#anonymousComplaintMessage");if(!category){message.textContent="Select an approved complaint category.";message.classList.add("error");return;}try{const details=[f.get("details"),f.get("location")&&`Location: ${f.get("location")}`,f.get("incidentDate")&&`Incident date: ${f.get("incidentDate")}`].filter(Boolean).join("\n");const result=await write("/api/anonymous-complaints",{title:`${category.name} complaint`,description:details,categoryId:category.id,departmentId:f.get("department")});message.classList.remove("error");message.textContent=`Complaint Submitted Successfully. Your complaint was automatically forwarded to ${result.administrativeUnit}. Complaint ID: ${result.complaintId}. Private tracking code: ${result.trackingCode}`;anonymousForm.reset();}catch(error){message.classList.add("error");message.textContent=error.message;}});
  }
  document.querySelectorAll(".tc-faq-item").forEach((item,index)=>{
    const button=item.querySelector(".tc-faq-q"),answer=item.querySelector(".tc-faq-a");
    if(!button||!answer)return;
    const answerId=`track-faq-answer-${index+1}`;
    button.type="button";button.setAttribute("aria-controls",answerId);button.setAttribute("aria-expanded","false");
    answer.id=answerId;answer.setAttribute("role","region");answer.setAttribute("aria-labelledby",`track-faq-question-${index+1}`);answer.hidden=true;
    button.id=`track-faq-question-${index+1}`;
    button.addEventListener("click",()=>{const open=button.getAttribute("aria-expanded")==="true";button.setAttribute("aria-expanded",String(!open));answer.hidden=open;answer.classList.toggle("open",!open);});
  });
})();
