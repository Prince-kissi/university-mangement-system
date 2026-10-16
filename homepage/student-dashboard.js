(function () {
  "use strict";
  let student;
  let complaints = [];
  const esc = value => String(value ?? "").replace(/[&<>'"]/g, c => ({"&":"&amp;","<":"&lt;",">":"&gt;","'":"&#39;",'"':"&quot;"})[c]);
  const fmt = value => value ? new Intl.DateTimeFormat("en-GH", { dateStyle:"medium", timeZone:"Africa/Accra" }).format(new Date(value)) : "Not available";
  const initials = name => name.split(/\s+/).filter(Boolean).slice(0,2).map(x=>x[0]).join("").toUpperCase() || "ST";
  async function request(path, options={}) {
    const response=await fetch(path,{credentials:"include",cache:"no-store",...options});
    if(response.status===401||response.status===403){location.replace("login.html");throw new Error("Session expired");}
    if(!response.ok){let message=`Request failed (${response.status})`;try{const b=await response.json();message=b.detail||b.message||b.title||message;}catch{}throw new Error(message);}
    return response.status===204?null:response.json();
  }
  async function write(path,method,body){const csrf=await request("/api/auth/csrf");return request(path,{method,headers:{"Content-Type":"application/json","X-CSRF-TOKEN":csrf.token},body:JSON.stringify(body)});}
  function showSection(id){document.querySelectorAll(".dashboard-section").forEach(x=>x.classList.toggle("active",x.id===id));document.querySelectorAll("[data-section]").forEach(x=>x.classList.toggle("active",x.dataset.section===id));document.querySelector("#dashboardSidebar")?.classList.remove("open");}
  function renderProfile(p){
    const name=`${p.firstName||""} ${p.lastName||""}`.trim(); const avatar=initials(name);
    document.querySelector("#dashboardTopAvatar").textContent=avatar; document.querySelector("#dashboardTopName").textContent=name;
    document.querySelector("#dashboardTopIndex").textContent=p.referenceNumber||p.email||"";
    const profileAvatar=document.querySelector("#profileAvatar"); if(p.profileImageUrl){profileAvatar.classList.add("has-photo");profileAvatar.innerHTML=`<img src="${esc(p.profileImageUrl)}?v=${Date.now()}" alt="Profile photo">`;}else{profileAvatar.classList.remove("has-photo");profileAvatar.textContent=avatar;} document.querySelector("#profileDisplayName").textContent=name;
    document.querySelector("#profileDisplayReference").textContent=p.referenceNumber||"";
    document.querySelector("#profileFullName").value=name; document.querySelector("#profileReferenceNumber").value=p.referenceNumber||"";
    document.querySelector("#profileIndexNumber").value=p.indexNumber||"";
    document.querySelector("#profileDateOfBirth").value=p.dateOfBirth?.slice(0,10)||""; document.querySelector("#profileLevel").value=p.level||"";
    document.querySelector("#profileTelephoneNumber").value=p.telephoneNumber||""; document.querySelector("#profileAccountCreationDate").value=p.accountCreationDate?.slice(0,10)||"";
    document.querySelector("#profileStudentEmail").value=p.email||"";
    document.querySelector("#profileProgramme").value=p.programme||"";
    document.querySelector("#profileFacultyOrSchool").value=p.facultyOrSchool||"";
    document.querySelector("#profileAdmissionYear").value=p.admissionYear||"";
    document.querySelector("#profilePersonalEmail").value=p.personalEmail||"";
    document.querySelector("#profileResidentialAddress").value=p.residentialAddress||"";
    const department=document.querySelector("#profileDepartment"); if(department){const option=[...department.options].find(x=>x.textContent===p.department||x.value===p.department);department.value=option?.value||"";}
    document.querySelector("#overviewTitle").textContent=`${window.StudentGreeting.getStudentGreeting(p.firstName||"Student")}.`;
    document.querySelector("#dashboardDate").textContent=new Intl.DateTimeFormat("en-GB",{weekday:"long",day:"numeric",month:"long",year:"numeric"}).format(new Date());
  }
  function complaintRow(c,history=false){
    const status=String(c.status||""); const href=`track-complaint.html?id=${encodeURIComponent(c.id)}`;
    if(history)return `<div class="history-row" role="row"><strong>${esc(c.title)}<small>${esc(c.complaintId)}</small></strong><span>${esc(c.department)}</span><span>${fmt(c.createdAt)}</span><span class="status-pill">${esc(status)}</span><a href="${href}">View</a></div>`;
    return `<article class="complaint-row" data-search="${esc(`${c.title} ${c.complaintId}`.toLowerCase())}"><div class="complaint-badge">${esc(initials(c.category||"Complaint"))}</div><div class="complaint-info"><strong>${esc(c.title)}</strong><span>${esc(c.complaintId)} - ${esc(c.department)}</span></div><span class="status-pill">${esc(status)}</span><time>${fmt(c.createdAt)}</time><a href="${href}">View</a></article>`;
  }
  function renderDashboard(d,notifications){
    const metrics=[["Total complaints",d.totalComplaints],["Under review",d.underReview],["Resolved",d.resolved],["Action required",d.actionRequired]];
    document.querySelector("#studentMetrics").innerHTML=metrics.map(([n,v])=>`<article class="metric-card"><div><small>${esc(n)}</small><strong>${esc(v||0)}</strong></div></article>`).join("");
    complaints=d.recentActivity||[]; document.querySelector("#recentComplaints").innerHTML=complaints.length?complaints.map(c=>complaintRow(c)).join(""):'<p class="search-empty">No complaints found.</p>';
    document.querySelector("#complaintHistory").innerHTML=complaints.length?complaints.map(c=>complaintRow(c,true)).join(""):'<div class="history-row"><span>No complaints found.</span></div>';
    document.querySelector("#complaintNavCount").textContent=d.totalComplaints||0;
    const unread=notifications.filter(n=>!n.isRead),navBadge=document.querySelector("#notificationNavCount"),topBadge=document.querySelector("#notificationTopCount");navBadge.textContent=unread.length;topBadge.textContent=unread.length;navBadge.hidden=unread.length===0;topBadge.hidden=unread.length===0;
    const notificationHtml=notifications.length?notifications.map(n=>`<article class="dashboard-card notification-item ${n.isRead?"":"unread"}" data-id="${esc(n.id)}"><span class="notification-symbol">${esc(initials(n.title))}</span><div><strong>${esc(n.title)}</strong><p>${esc(n.message)}</p><small>${fmt(n.createdAt)}</small></div>${n.isRead?"":'<span class="unread-dot"></span>'}</article>`).join(""):'<article class="dashboard-card notification-item"><p>No notifications found.</p></article>';
    document.querySelector("#studentNotifications").innerHTML=notificationHtml;
    document.querySelector("#latestNotification").innerHTML=notifications[0]?`<div class="card-heading"><div><span>LATEST UPDATE</span><h2>${esc(notifications[0].title)}</h2></div></div><p>${esc(notifications[0].message)}</p><small>${fmt(notifications[0].createdAt)}</small>`:'<p>No notifications found.</p>';
  }
  async function start(){
    try{const [profile,dashboard,notifications]=await Promise.all([request("/api/students/me"),request("/api/complaints/dashboard"),request("/api/notifications")]);student=profile;renderProfile(profile);renderDashboard(dashboard,notifications);const welcome=sessionStorage.getItem("umatAccountCreatedWelcome");if(welcome){const el=document.querySelector("#accountCreatedMessage");el.textContent=welcome;el.hidden=false;sessionStorage.removeItem("umatAccountCreatedWelcome");}}
    catch(error){document.querySelector("#overviewTitle").textContent=`Unable to load live data: ${error.message}`;}
  }
  document.querySelectorAll("[data-section]").forEach(x=>x.addEventListener("click",e=>{e.preventDefault();showSection(x.dataset.section);}));
  document.querySelectorAll("[data-section-button]").forEach(x=>x.addEventListener("click",()=>showSection(x.dataset.sectionButton)));
  document.querySelector(".dashboard-menu")?.addEventListener("click",()=>document.querySelector("#dashboardSidebar")?.classList.toggle("open"));
  document.querySelector("#complaintSearch")?.addEventListener("input",e=>{const q=e.target.value.trim().toLowerCase();document.querySelectorAll(".complaint-row").forEach(row=>row.hidden=!row.dataset.search.includes(q));});
  document.querySelector("#markAllRead")?.addEventListener("click",async()=>{const unread=[...document.querySelectorAll(".notification-item.unread")];await Promise.all(unread.map(x=>write(`/api/notifications/${x.dataset.id}/read`,"POST",{})));location.reload();});
  document.querySelector("#studentNotifications")?.addEventListener("click",async event=>{const item=event.target.closest(".notification-item[data-id]");if(!item||!item.classList.contains("unread"))return;await write(`/api/notifications/${item.dataset.id}/read`,"POST",{});location.reload();});
  document.querySelector("#profileForm")?.addEventListener("submit",async e=>{e.preventDefault();const formData=new FormData(e.currentTarget),names=String(formData.get("fullName")||"").trim().split(/\s+/),message=document.querySelector("#profileMessage");try{student=await write("/api/auth/profile","PUT",{firstName:names.shift()||student.firstName,lastName:names.join(" ")||student.lastName,facultyOrSchool:formData.get("facultyOrSchool")||null,admissionYear:formData.get("admissionYear")?Number(formData.get("admissionYear")):null,personalEmail:formData.get("personalEmail")||null,residentialAddress:formData.get("residentialAddress")||null});renderProfile(student);message.classList.remove("error");message.textContent="Profile saved to the database.";}catch(error){message.classList.add("error");message.textContent=error.message;}});
  document.querySelector("#uploadProfilePhoto")?.addEventListener("click",async()=>{const input=document.querySelector("#profilePhoto"),message=document.querySelector("#profileMessage"),file=input.files[0];if(!file){message.classList.add("error");message.textContent="Choose a profile photo first.";return;}if(file.size>2*1024*1024){message.classList.add("error");message.textContent="The profile photo must not exceed 2 MB.";return;}const body=new FormData();body.append("photo",file);try{const csrf=await request("/api/auth/csrf");await request("/api/profile/photo",{method:"PUT",headers:{"X-CSRF-TOKEN":csrf.token},body});student.profileImageUrl="/api/profile/photo";renderProfile(student);message.classList.remove("error");message.textContent="Profile photo updated successfully.";input.value="";}catch(error){message.classList.add("error");message.textContent=error.message;}});
  document.querySelector("#dashboardPasswordForm")?.addEventListener("submit",async e=>{e.preventDefault();const body={currentPassword:document.querySelector("#dashboardCurrentPassword")?.value||"",newPassword:document.querySelector("#dashboardNewPassword").value,confirmPassword:document.querySelector("#dashboardConfirmPassword").value};const message=document.querySelector("#passwordMessage");try{await write("/api/auth/change-password","POST",body);message.textContent="Password changed successfully.";e.currentTarget.reset();}catch(error){message.textContent=error.message;message.classList.add("error");}});
  document.querySelectorAll("[data-student-logout]").forEach(x=>x.addEventListener("click",async e=>{e.preventDefault();try{await write("/api/auth/logout","POST",{});}finally{location.href="login.html";}}));
  start();
})();
