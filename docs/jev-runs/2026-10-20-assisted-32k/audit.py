"""Read the archived journal and audit cash and inputs before upper-level production."""
import collections
from decimal import Decimal, getcontext
import gzip
import json
from pathlib import Path

getcontext().prec = 50
root = Path(__file__).resolve().parent
config = json.loads((root / 'production-model.json').read_text())
outputs = {r['Id']: r['OutputMaterialId'] for r in config['Recipes']}
stocks = collections.defaultdict(Decimal)
factory_recipes = {}
costs = collections.defaultdict(Decimal)
income = Decimal(0)
turn, phase = 1, 'Settlement'
observed = []
fields = {
    'FactoryBuilt': ('construction', 'Cost'),
    'WorkersHired': ('hiring', 'Cost'),
    'WorkersFired': ('firing', 'Cost'),
    'SalariesPaid': ('salaries', 'Amount'),
    'FactoryUpkeepPaid': ('upkeep', 'Amount'),
    'GenerationResearchInvested': ('research', 'Amount'),
    'FactoryOverhaulStarted': ('overhaul', 'Cost'),
    'FactoryProduced': ('electricity', 'OverheadCost'),
}
for line in gzip.decompress((root / 'journal.jsonl.gz').read_bytes()).decode().splitlines():
    entry = json.loads(line)
    kind = entry['ChangeType'].split(',')[0].split('.')[-1]
    value = json.loads(entry['ChangeJson'], parse_float=Decimal)
    if kind in ('FactoryBuilt', 'RecipeSelected'):
        factory_recipes[value['FactoryId']] = value['RecipeId']
    if kind == 'PhaseAdvanced':
        if phase == 'Settlement':
            phase = 'Decision'
        elif turn < 98:
            phase, turn = 'Settlement', turn + 1
    if kind == 'FactoryProduced':
        material = outputs[factory_recipes[value['FactoryId']]]
        if material == 'steel-structure':
            observed.append(dict(turn=turn, capacity=str(value['CapacityLimitedOutputQuantity']),
                                 output=str(value['OutputQuantity']),
                                 sheet_before=str(stocks['hot-rolled-sheet']),
                                 bar_before=str(stocks['forging-bar'])))
        for material_id, quantity in value['ConsumedInputs'].items():
            stocks[material_id] -= quantity
        stocks[material] += value['OutputQuantity']
    if kind == 'MaterialSoldToSystem':
        stocks[value['MaterialId']] -= value['Volume']
        income += value['TotalRevenue']
    if kind in fields:
        category, field = fields[kind]
        costs[category] += value[field]
    if kind == 'FactoryRepairTurnPassed':
        costs['repair_salaries'] += value['SalaryPaid']
        costs['repair_upkeep'] += value['UpkeepPaid']
result = json.loads((root / 'result.json').read_text(), parse_float=Decimal)
reconstructed = income - sum(costs.values())
assert abs(reconstructed - result['Balance']) < Decimal('0.000000000001')
working = [x for x in observed if Decimal(x['capacity']) > 0]
assert len(working) == 20
assert all(Decimal(x['sheet_before']) <= 0 or Decimal(x['bar_before']) <= 0 for x in working)
audit = dict(income=str(income), expenses={k: str(v) for k, v in costs.items()},
             reconstructed_balance=str(reconstructed), upper_factory_inputs=observed)
(root / 'audit.json').write_text(json.dumps(audit, indent=2) + '\n')
print(json.dumps(audit, indent=2))
