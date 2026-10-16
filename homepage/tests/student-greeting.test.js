const test = require('node:test');
const assert = require('node:assert/strict');
const { getStudentGreeting } = require('../student-greeting.js');

test('uses the morning greeting in Ghana time', () => {
  assert.equal(getStudentGreeting('Ama', new Date('2026-07-17T11:59:00Z')), 'Good morning, Ama');
});

test('uses the afternoon greeting in Ghana time', () => {
  assert.equal(getStudentGreeting('Ama', new Date('2026-07-17T12:00:00Z')), 'Good afternoon, Ama');
});

test('uses the evening greeting in Ghana time', () => {
  assert.equal(getStudentGreeting('Ama', new Date('2026-07-17T17:00:00Z')), 'Good evening, Ama');
});
