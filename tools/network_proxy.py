"""Loopback-only UDP impairment for isolated native ENet QA, never game input."""
import heapq
import random
import selectors
import socket
import threading
import time


class NetworkProxy:
    def __init__(self, host_port, clients, latency_ms, jitter_ms, loss_percent,
                 blackout_seconds=0, trigger=None, shutdown_ready=None):
        self.config = dict(latency_ms=latency_ms, jitter_ms=jitter_ms,
                           loss_percent=loss_percent, blackout_seconds=blackout_seconds,
                           blackout_trigger='fervor-0-prepared-host' if blackout_seconds else None,
                           random_seed=2868840)
        self.host = ('127.0.0.1', host_port)
        self.trigger = trigger
        self.shutdown_ready = shutdown_ready or (lambda: False)
        self.selector = selectors.DefaultSelector()
        self.links = []
        self.queue = []
        self.random = random.Random(self.config['random_seed'])
        self.sequence = 0
        self.blackout_start = None
        self.blackout_end = None
        self.events = []
        self.errors = []
        self.stop_event = threading.Event()
        self.thread = None
        for index in range(clients):
            front = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
            back = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
            front.bind(('127.0.0.1', 0))
            back.bind(('127.0.0.1', 0))
            back.connect(self.host)
            front.setblocking(False)
            back.setblocking(False)
            link = dict(front=front, back=back, client=None, index=index + 1,
                        directions={name: dict(received=0, forwarded=0, lost=0, blackout=0,
                                               bytes_forwarded=0, max_delay_ms=0)
                                    for name in ('client_to_host', 'host_to_client')})
            self.links.append(link)
            self.selector.register(front, selectors.EVENT_READ, (link, 'client_to_host'))
            self.selector.register(back, selectors.EVENT_READ, (link, 'host_to_client'))
        self.client_ports = [link['front'].getsockname()[1] for link in self.links]

    def start(self):
        self.thread = threading.Thread(target=self._run, daemon=True, name='qa-enet-proxy')
        self.thread.start()

    def _blackout(self, now):
        if self.blackout_start is None and self.config['blackout_seconds'] and self.trigger.exists():
            self.blackout_start = now
            self.blackout_end = now + self.config['blackout_seconds']
            self.events.append(dict(event='blackout.start', monotonic=now))
        active = self.blackout_start is not None and now < self.blackout_end
        if self.blackout_end is not None and now >= self.blackout_end:
            if self.events[-1]['event'] != 'blackout.end':
                self.events.append(dict(event='blackout.end', monotonic=now))
        return active

    def _run(self):
        try:
            while not self.stop_event.is_set():
                self._blackout(time.monotonic())
                for key, _ in self.selector.select(.005):
                    link, direction = key.data
                    data, address = key.fileobj.recvfrom(65535)
                    if direction == 'client_to_host':
                        if link['client'] is not None and link['client'] != address:
                            raise RuntimeError('Unexpected second client on one QA proxy')
                        link['client'] = address
                    stats = link['directions'][direction]
                    stats['received'] += 1
                    now = time.monotonic()
                    if self._blackout(now):
                        stats['blackout'] += 1
                        continue
                    if self.random.random() * 100 < self.config['loss_percent']:
                        stats['lost'] += 1
                        continue
                    delay = (self.config['latency_ms'] + self.random.uniform(
                        -self.config['jitter_ms'], self.config['jitter_ms'])) / 1000
                    self.sequence += 1
                    heapq.heappush(self.queue, (now + delay, self.sequence, now, link, direction, data))
                now = time.monotonic()
                while self.queue and self.queue[0][0] <= now:
                    _, _, received_at, link, direction, data = heapq.heappop(self.queue)
                    stats = link['directions'][direction]
                    if self._blackout(now):
                        stats['blackout'] += 1
                        continue
                    if direction == 'client_to_host':
                        link['back'].send(data)
                    else:
                        link['front'].sendto(data, link['client'])
                    stats['forwarded'] += 1
                    stats['bytes_forwarded'] += len(data)
                    stats['max_delay_ms'] = round(max(stats['max_delay_ms'], (now - received_at) * 1000), 2)
        except ConnectionResetError as exc:
            # Windows reports an ICMP port-unreachable on connected UDP when
            # the native host closes. Accept it only after every peer has
            # completed the final QA barrier, with the original detail retained.
            if self.shutdown_ready():
                self.events.append(dict(event='udp.endpoint_closed_after_checks',
                                        monotonic=time.monotonic(), detail=repr(exc)))
            else:
                self.errors.append(repr(exc))
        except Exception as exc:
            self.errors.append(repr(exc))

    def stop(self):
        self.stop_event.set()
        if self.thread:
            self.thread.join(timeout=2)
            if self.thread.is_alive():
                self.errors.append('QA proxy thread failed to stop')
        self.selector.close()
        for link in self.links:
            link['front'].close()
            link['back'].close()

    def report(self):
        return dict(config=self.config, events=self.events, errors=self.errors,
                    peers=[dict(index=link['index'], directions=link['directions']) for link in self.links])

    def validation_errors(self):
        errors = list(self.errors)
        directions = [stats for link in self.links for stats in link['directions'].values()]
        if any(stats['forwarded'] == 0 for stats in directions):
            errors.append('Some peer directions did not traverse the proxy')
        if self.config['loss_percent'] and not sum(stats['lost'] for stats in directions):
            errors.append('Configured random packet loss was not exercised')
        if self.config['blackout_seconds']:
            if not any(event['event'] == 'blackout.end' for event in self.events):
                errors.append('Combat blackout did not finish')
            if not sum(stats['blackout'] for stats in directions):
                errors.append('Combat blackout did not interrupt any real datagrams')
        return errors
