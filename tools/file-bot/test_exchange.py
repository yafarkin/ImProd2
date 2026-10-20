import hashlib
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

SCRIPT = Path(__file__).with_name('exchange.py')


class ExchangeTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.directory = Path(self.temp.name)
        (self.directory/'001.request.json').write_text('{"requestId":"turn-1","state":"тест"}')
        self.run_gate('init')

    def tearDown(self):
        self.temp.cleanup()

    def run_gate(self, operation, turn=None, text=None, success=True, extra=None):
        args = [sys.executable, str(SCRIPT), operation, str(self.directory)]
        if turn is not None:
            args.append(str(turn))
        if extra is not None:
            args.extend(extra)
        result = subprocess.run(args, input=text, text=True, capture_output=True)
        self.assertEqual(result.returncode == 0, success, result.stderr)
        return result

    def test_custom_limits_are_recorded_and_horizon_is_enforced(self):
        with tempfile.TemporaryDirectory() as temp:
            (Path(temp) / '001.request.json').write_text('{"requestId":"turn-1"}')
            result = subprocess.run(
                [sys.executable, str(SCRIPT), 'init', temp,
                 '--limit', '40000', '--max-response', '800', '--max-turns', '30'],
                text=True, capture_output=True)
            self.assertEqual(result.returncode, 0, result.stderr)
            budget = json.loads((Path(temp) / 'budget.json').read_text())
            self.assertEqual((budget['limit'], budget['max_response'], budget['max_turns']),
                             (40000, 800, 30))
            # Ход за согласованным горизонтом не выдаётся.
            beyond = subprocess.run([sys.executable, str(SCRIPT), 'show', temp, '31'],
                                    text=True, capture_output=True)
            self.assertNotEqual(beyond.returncode, 0)
            # Ответ крупнее оставленного резерва не одобряется — при кастомном запасе 800.
            oversize = '{"requestId":"turn-1","actions":[],"note":"' + 'x' * 20000 + '"}'
            subprocess.run([sys.executable, str(SCRIPT), 'show', temp, '1'],
                           text=True, capture_output=True)
            answer = subprocess.run([sys.executable, str(SCRIPT), 'answer', temp, '1'],
                                    input=oversize, text=True, capture_output=True)
            self.assertNotEqual(answer.returncode, 0)
            self.assertFalse((Path(temp) / '001.approved').exists())

    def test_delivery_and_approval_match_exact_bytes(self):
        self.run_gate('show', 1)
        response = '{"requestId":"turn-1","actions":[]}'
        self.run_gate('answer', 1, response)
        budget = json.loads((self.directory/'budget.json').read_text())
        import tiktoken
        encoder = tiktoken.get_encoding('o200k_base')
        self.assertEqual(budget['used'], sum(len(encoder.encode((self.directory/f'001.{direction}.json').read_text()))
                                            for direction in ['request', 'response']))
        self.assertEqual((self.directory/'001.approved').read_text(), hashlib.sha256(response.encode()).hexdigest().upper())
        self.run_gate('show', 1, success=False)
        self.run_gate('answer', 1, response, success=False)

    def test_request_reserves_reply_and_stops_before_delivery(self):
        path = self.directory/'budget.json'
        budget = json.loads(path.read_text())
        budget['used'] = 7500
        path.write_text(json.dumps(budget))
        result = self.run_gate('show', 1)
        self.assertNotIn('"state"', result.stdout)
        self.assertTrue((self.directory/'STOP').exists())
        self.assertEqual(json.loads(path.read_text())['used'], 7500)

    def test_oversize_reply_never_gets_approved(self):
        self.run_gate('show', 1)
        self.run_gate('answer', 1, '{"requestId":"turn-1","actions":[],"extra":"' + 'x '*1500 + '"}', success=False)
        self.assertFalse((self.directory/'001.approved').exists())
        self.assertFalse((self.directory/'001.response.json').exists())
        self.assertTrue((self.directory/'STOP').exists())


if __name__ == '__main__':
    unittest.main()
