const scForm = document.getElementById('submitComplaintForm');
const scMsg = document.getElementById('submitComplaintMessage');
const scRef = document.getElementById('scRefCode');
const departmentSelect = scForm?.elements.department;
const categorySelect = scForm?.elements.category;
let complaintCategories = [];

async function readComplaintProblem(response, fallback) {
  try {
    const problem = await response.json();
    const messages = Object.values(problem.errors || {}).flat();
    return messages.length ? messages.join(' ') : problem.detail || problem.title || fallback;
  } catch { return fallback; }
}

function showComplaintFormMessage(title, message) {
  scMsg.querySelector('strong').textContent = title;
  scMsg.querySelector('p').textContent = message;
  scMsg.classList.add('show');
  scMsg.scrollIntoView({ behavior: 'smooth', block: 'center' });
}

function populateComplaintCategories() {
  const departmentId = departmentSelect.value;
  const categories = complaintCategories.filter((category) =>
    category.isActive && (!category.departmentId || category.departmentId === departmentId));
  categorySelect.replaceChildren(new Option(
    categories.length ? 'Select category' : 'No categories for this department', '', true, true));
  categorySelect.options[0].disabled = true;
  categories.forEach((category) => categorySelect.add(new Option(category.name, category.id)));
  categorySelect.disabled = categories.length === 0;
}

async function prepareComplaintForm() {
  const [profileResponse, departmentsResponse, categoriesResponse] = await Promise.all([
    fetch('/api/students/me', { credentials: 'include' }),
    fetch('/api/departments'),
    fetch('/api/categories')
  ]);
  if (profileResponse.status === 401 || profileResponse.status === 403) {
    location.replace('login.html');
    return;
  }
  if (!profileResponse.ok || !departmentsResponse.ok || !categoriesResponse.ok) {
    throw new Error('Unable to prepare complaint form');
  }
  const [profile, departments, categories] = await Promise.all([
    profileResponse.json(), departmentsResponse.json(), categoriesResponse.json()
  ]);
  complaintCategories = categories;
  departmentSelect.replaceChildren(new Option('Select department', '', true, true));
  departmentSelect.options[0].disabled = true;
  departments.filter((department) => department.isActive).forEach((department) =>
    departmentSelect.add(new Option(department.name, department.id)));
  scForm.elements.fullName.value = `${profile.firstName || ''} ${profile.lastName || ''}`.trim();
  scForm.elements.email.value = profile.email || '';
  scForm.elements.phone.value = profile.telephoneNumber || '';
  scForm.elements.year.value = profile.level ? `Year ${Number(profile.level) / 100}` : '';
  departmentSelect.addEventListener('change', populateComplaintCategories);
  populateComplaintCategories();
}

prepareComplaintForm().catch(() => showComplaintFormMessage(
  'Unable to load the complaint form.', 'Please refresh the page and try again.'));

scForm?.addEventListener('submit', async (event) => {
  event.preventDefault();
  const title = String(scForm.elements.title.value || '').trim();
  const description = String(scForm.elements.description.value || '').trim();
  if (!departmentSelect.value || !categorySelect.value || title.length < 5 || description.length < 20) {
    showComplaintFormMessage('Please complete the required complaint details.',
      'Use a title of at least 5 characters and a description of at least 20 characters.');
    return;
  }
  const submitButton = scForm.querySelector('[type="submit"]');
  submitButton.disabled = true;
  try {
    const csrfResponse = await fetch('/api/auth/csrf', { credentials: 'include' });
    const csrf = await csrfResponse.json();
    const response = await fetch('/api/complaints', {
      method: 'POST', credentials: 'include',
      headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': csrf.token },
      body: JSON.stringify({
        title, description,
        categoryId: categorySelect.value,
        departmentId: departmentSelect.value,
        priority: ({ low: 'Low', medium: 'Normal', high: 'High', critical: 'Critical' })[
          scForm.elements.severity.value || 'medium']
      })
    });
    if (!response.ok) throw new Error(await readComplaintProblem(response, 'The complaint could not be submitted.'));
    const result = await response.json();
    scRef.textContent = result.complaintId;
    showComplaintFormMessage('Complaint Submitted Successfully!',
      'Your complaint has been saved. Your dashboard will now show the updated statistics.');
    window.setTimeout(() => location.href = 'student-dashboard.html?submitted=1', 1200);
  } catch (error) {
    showComplaintFormMessage('Complaint not submitted.', error.message || 'Please try again.');
  } finally {
    submitButton.disabled = false;
  }
});

const fileInput = document.getElementById('scFileInput');
fileInput?.addEventListener('change', () => {
  const label = fileInput.closest('.sc-upload').querySelector('p');
  if (fileInput.files.length > 0) label.textContent = `${fileInput.files.length} file(s) selected`;
});
