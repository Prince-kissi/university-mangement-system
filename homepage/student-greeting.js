(function exposeStudentGreeting(root, factory) {
  const greeting = factory();
  if (typeof module === 'object' && module.exports) module.exports = greeting;
  else root.StudentGreeting = greeting;
}(typeof globalThis !== 'undefined' ? globalThis : this, function createStudentGreeting() {
  function getStudentGreeting(firstName, now = new Date()) {
    const hourText = new Intl.DateTimeFormat('en-GB', {
      timeZone: 'Africa/Accra', hour: '2-digit', hourCycle: 'h23'
    }).format(now);
    const hour = Number.parseInt(hourText, 10);
    if (hour < 12) return `Good morning, ${firstName}`;
    if (hour < 17) return `Good afternoon, ${firstName}`;
    return `Good evening, ${firstName}`;
  }

  return { getStudentGreeting };
}));
