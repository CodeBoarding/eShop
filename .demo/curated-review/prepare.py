"""Build a separately labelled demo review from the pinned original analysis."""
import json
import shutil
import sys
from pathlib import Path

review = Path(sys.argv[1])
metadata = json.loads((review / 'metadata.json').read_text())
head = '1fd62bfcac833400d98d7985ff1547b78eccaa6a'
assert str(metadata['pr_number']) == '11' and metadata['head_sha'] == head
architecture = review / 'closed/architecture.json'
shutil.copyfile(architecture, review / 'original_architecture.json')

def finding(id, severity, title, impact, fact, fix, file, line, change, components, expand, edges=()):
    return dict(id=id, source='model', kind='Curated demo finding',
                riskKind='Customer action can fail', severity=severity,
                title='[Curated demo] ' + title, impact=impact, fact=fact, fix=fix,
                anchors=dict(code=dict(file=file, line=line), flow=dict(change=change),
                             map=dict(components=components, expand=expand, edges=list(edges))))

findings = [
    finding('curated-stale-item-update', 'high',
        'Editing a product can make a reserved item available again',
        'A customer reserves the last item. An administrator saves an older product form, restoring its available quantity. Another customer can now reserve the same item.',
        'CatalogApi.UpdateItem loads the current database row, then copies every value from the request with SetValues. A payload captured before the reservation can overwrite AvailableStock. The concurrency token compares against the freshly loaded database value, so it does not detect this stale client payload.',
        'Keep available quantity out of general product edits. Change it through a dedicated operation, or require the version originally read by the client and reject stale updates.',
        'src/Catalog.API/Apis/CatalogApi.cs', 386, 'u6', ['3.2'], ['3']),
    finding('curated-order-replay-basket', 'high',
        'Retrying an old order event can empty the customer’s new basket',
        'After checkout, the customer starts shopping again. If the old order-start event is replayed after a crash, the basket service deletes their new items too.',
        'OrderingOutboxWorker republishes transactions whose events are not marked Published. A crash after publish but before that mark can replay OrderStartedIntegrationEvent. The unchanged Basket handler at OrderStartedIntegrationEventHandler.cs:14 deletes the current basket by UserId, without checking the event ID or which basket was checked out.',
        'Make basket clearing safe to repeat: track processed event IDs and clear only the basket version associated with that checkout, with an atomic check and update.',
        'src/Ordering.API/Application/IntegrationEvents/OrderingOutboxWorker.cs', 23, 'u9',
        ['2.1', '3.3', '6.2', '6.1', '7.2', '7.5'], ['2', '3', '6', '7'],
        [('2.1', '3.3'), ('2.1', '6.2'), ('6.2', '6.1'), ('6.1', '7.2'), ('7.2', '7.5')]),
    finding('curated-reservation-history-delete', 'medium',
        'A completed reservation can prevent product deletion',
        'An administrator tries to remove a product after its reservation has completed or been released. The new reservation history still holds a database link, so the existing delete operation fails.',
        'ReservedItem.ProductId uses DeleteBehavior.Restrict. CompleteAsync and ReleaseAsync retain the ReservedItems rows, while CatalogApi.DeleteItemById still physically deletes the catalog item. The foreign key therefore blocks deletion even when no reservation is active.',
        'Define the product retirement policy explicitly: archive products, or retain terminal reservation snapshots without a restrictive link to the live catalog row. Preserve the reservation audit history.',
        'src/Catalog.API/Reservations/ItemReservationConfiguration.cs', 23, 'u1', ['3.2'], ['3']),
]
envelope = dict(feature='architecture', schema=1, status='partial', repo='CodeBoarding/eShop',
                pr=11, headSha=head, generator=None, coverage=None,
                reason='Curated demo findings: AI-assisted editorial review of three code-backed scenarios. These were not detected by the original CodeBoarding run; this is not a complete review.',
                data=dict(findings=findings))
architecture.write_text(json.dumps(envelope, indent=2) + '\n')
provenance = dict(kind='curated-demo', method='AI-assisted editorial curation',
                  original_run='45651302005', original_artifact='15041612761',
                  head_sha=head, automated_detection=False,
                  note='Original graph and change guide retained. Three curated findings replace the architecture findings only in this separate artifact. Original findings are preserved in original_architecture.json.')
(review / 'curation.json').write_text(json.dumps(provenance, indent=2) + '\n')
metadata['curation'] = provenance
(review / 'metadata.json').write_text(json.dumps(metadata, indent=2) + '\n')
guide_path = review / 'review_guide.json'
guide = json.loads(guide_path.read_text())
guide['summary'] = 'CURATED DEMO FINDINGS · ' + guide['summary']
guide_path.write_text(json.dumps(guide, indent=2) + '\n')
print('Prepared three labelled curated findings; original findings and graphs preserved.')
