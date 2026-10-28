import { axeViolations } from './axe';

describe('axeViolations', () => {
  afterEach(() => {
    document.body.replaceChildren();
  });

  function mount(html: string): HTMLElement {
    const root = document.createElement('div');
    root.innerHTML = html;
    document.body.append(root);
    return root;
  }

  it('reports what the primitives must never do, so a clean result means something', async () => {
    const root = mount('<input type="text" /><button></button>');
    const violations = (await axeViolations(root)).map((violation) => violation.split(':')[0]);
    expect(violations).toContain('label');
    expect(violations).toContain('button-name');
  });

  it('passes a labelled control', async () => {
    const root = mount(
      '<label for="a">Name</label><input id="a" type="text" /><button>Save</button>',
    );
    expect(await axeViolations(root)).toEqual([]);
  });
});
