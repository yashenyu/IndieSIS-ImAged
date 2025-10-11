async function login(){
  const u = document.getElementById('username').value;
  const p = document.getElementById('password').value;
  document.getElementById('status').textContent = 'Logging in...';
  try {
    const res = await fetch('http://localhost:8080/api/login', {
      method: 'POST',
      headers: {'Content-Type':'application/json'},
      body: JSON.stringify({ username: u, password: p })
    });

    if (!res.ok) {
      document.getElementById('status').textContent = 'Login failed';
      document.getElementById('token').textContent = '';
      return;
    }

    const data = await res.json();
    document.getElementById('status').textContent = 'Login successful — token (also saved to server session file):';
    document.getElementById('token').textContent = data.token;
  } catch (e) {
    document.getElementById('status').textContent = 'Error: ' + e;
  }
}
