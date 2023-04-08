const https = require('http');

const username = 'bam'

const baseUrl = 'http://localhost:5001/sms';
const headers = {
  'X-RapidAPI-User': username
};

let startId = 1;

async function getUpdates() {
  const url = `${baseUrl}/getupdates?start_id=${startId}`;
  const response = await sendRequest(url);
  const body = JSON.parse(response);

  if (body.success) {
    for (const messageId of body.messages) {
      await getMessage(messageId);
    }
  }
}

async function getMessage(messageId) {
  const url = `${baseUrl}/getmessage?message_id=${messageId}`;
  const response = await sendRequest(url);
  const body = JSON.parse(response);
  
  console.log(body); // Do something with the response

  startId = messageId + 1;
}

function sendRequest(url) {
  return new Promise((resolve, reject) => {
    https.get(url, { headers }, (res) => {
      let body = '';
      res.on('data', (chunk) => {
        body += chunk;
      });
      res.on('end', () => {
        resolve(body);
      });
      res.on('error', (error) => {
        reject(error);
      });
    });
  });
}

async function loop() {
  while (true) {
    await getUpdates();
//    await new Promise(resolve => setTimeout(resolve, 5000)); 
  }
}

loop().catch((error) => {
  console.error(error);
  process.exit(1);
});
